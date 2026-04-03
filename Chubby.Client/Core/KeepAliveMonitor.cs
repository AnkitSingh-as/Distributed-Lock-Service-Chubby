using Chubby.Protos;
using Grpc.Core;
using Microsoft.Extensions.Logging;

public class KeepAliveMonitor : IDisposable
{
    private readonly ChubbyClient _chubby;
    private readonly ILogger<KeepAliveMonitor> _logger;
    private readonly CancellationTokenSource cts;
    private long _ackedThroughSequence;

    private CancellationTokenSource? timerCts;
    private CancellationTokenSource? jeopardyCts;
    private AsyncDuplexStreamingCall<KeepAliveRequest, KeepAliveResponse>? stream;
    private Task? _recoveryTask;

    public KeepAliveMonitor(ChubbyClient chubbyClient, ILogger<KeepAliveMonitor> logger)
    {
        _chubby = chubbyClient;
        _logger = logger;
        cts = new CancellationTokenSource();
    }

    public async Task Initialize()
    {
        _logger.LogInformation("Initializing KeepAliveMonitor for session {SessionId}.", _chubby.SessionId);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
        stream = _chubby.KeepAlive(null, linkedCts.Token);
        if (!await Call(linkedCts.Token))
        {
            return;
        }

        await ReadResponse(linkedCts.Token);
    }

    public async Task<bool> Call(CancellationToken token)
    {
        if (stream is null || token.IsCancellationRequested)
        {
            return false;
        }

        try
        {
            _logger.LogDebug(
                "Sending keep-alive for session {SessionId} with ackedThroughSequence {AckedThroughSequence}.",
                _chubby.SessionId,
                Volatile.Read(ref _ackedThroughSequence));
            await stream.RequestStream.WriteAsync(new KeepAliveRequest
            {
                SessionId = _chubby.SessionId,
                AckedThroughSequence = Volatile.Read(ref _ackedThroughSequence)
            }, token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (RpcException)
        {
            _logger.LogWarning("Keep-alive write failed for session {SessionId}. Entering jeopardy recovery.", _chubby.SessionId);
            await GoInJeopardyState(cts.Token);
            return false;
        }
    }

    public async Task ReadResponse(CancellationToken token)
    {
        if (stream is null || token.IsCancellationRequested)
        {
            return;
        }

        try
        {
            await foreach (var response in stream.ResponseStream.ReadAllAsync(token))
            {
                switch (response.ReasonCase)
                {
                    case KeepAliveResponse.ReasonOneofCase.EventAvailable:
                        _logger.LogInformation(
                            "Received keep-alive event batch with {EventCount} event(s) for session {SessionId}.",
                            response.EventAvailable.Events.Count,
                            _chubby.SessionId);
                        var deliveredEvents = response.EventAvailable.Events.ToList();
                        var events = deliveredEvents.Select(deliveredEvent => deliveredEvent.Event).ToList();
                        _chubby.ProcessEvents(events);
                        if (deliveredEvents.Count > 0)
                        {
                            Volatile.Write(ref _ackedThroughSequence, deliveredEvents.Max(deliveredEvent => deliveredEvent.SequenceNumber));
                        }
                        if (events.Any(ev => ev.PayloadCase is Event.PayloadOneofCase.MasterFailOver))
                        {
                            _logger.LogInformation("Received MasterFailOver Event");
                        }

                        if (!await Call(token))
                        {
                            return;
                        }

                        if (_chubby.SessionState == SessionState.Jeopardy)
                        {
                            CompleteRecovery();
                        }

                        break;

                    case KeepAliveResponse.ReasonOneofCase.LeaseAboutToExpire:
                        _logger.LogDebug(
                            "Received LeaseAboutToExpire for session {SessionId} with lease timeout {LeaseTimeout}.",
                            _chubby.SessionId,
                            response.LeaseAboutToExpire.LeaseTimeout);
                        timerCts?.Cancel();
                        timerCts?.Dispose();
                        timerCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
                        _ = StartTimer(response.LeaseAboutToExpire.LeaseTimeout, timerCts.Token);

                        if (!await Call(token))
                        {
                            return;
                        }

                        if (_chubby.SessionState == SessionState.Jeopardy)
                        {
                            CompleteRecovery();
                        }

                        break;

                    default:
                        _logger.LogError("Received unsupported keep-alive response reason {ReasonCase}.", response.ReasonCase);
                        throw new NotSupportedException();
                }
            }

            if (!token.IsCancellationRequested)
            {
                _logger.LogWarning("Keep-alive stream completed for session {SessionId}. Entering jeopardy recovery.", _chubby.SessionId);
                await GoInJeopardyState(cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (RpcException)
        {
            _logger.LogWarning("Keep-alive stream failed for session {SessionId}. Entering jeopardy recovery.", _chubby.SessionId);
            await GoInJeopardyState(cts.Token);
        }
    }

    private async Task StartTimer(int masterLeaseTimeout, CancellationToken token)
    {
        try
        {
            await Task.Delay(GetTimeToWait(masterLeaseTimeout), token);
            _logger.LogWarning("Keep-alive timer elapsed for session {SessionId}. Entering jeopardy.", _chubby.SessionId);
            await GoInJeopardyState(token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
    }

    private Task GoInJeopardyState(CancellationToken token)
    {
        if (_chubby.SessionState == SessionState.Error || _chubby.SessionState == SessionState.Jeopardy)
        {
            return Task.CompletedTask;
        }

        // A recovered keep-alive stream may talk to a new master with a fresh in-memory
        // event sequence. Do not carry over acknowledgments from the old stream.
        Volatile.Write(ref _ackedThroughSequence, 0);
        _chubby.SessionState = SessionState.Jeopardy;
        _logger.LogWarning("Client session {SessionId} entered Jeopardy state.", _chubby.SessionId);
        stream?.Dispose();

        jeopardyCts?.Cancel();
        jeopardyCts?.Dispose();
        jeopardyCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, token);

        _ = GoInErrorStateIfRequired(jeopardyCts.Token);
        _recoveryTask = RecoverLoopAsync(jeopardyCts.Token);
        return Task.CompletedTask;
    }

    private async Task RecoverLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                stream?.Dispose();
                _logger.LogInformation("Attempting keep-alive recovery for session {SessionId}.", _chubby.SessionId);
                await Initialize();
                if (_chubby.SessionState == SessionState.Normal)
                {
                    return;
                }

                await Task.Delay(1000, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                await Task.Delay(1000, token);
            }
        }
    }

    private async Task GoInErrorStateIfRequired(CancellationToken token)
    {
        try
        {
            await Task.Delay(GetTimeToWait(45), token);
            stream?.Dispose();
            _chubby.SessionState = SessionState.Error;
            _logger.LogError("Client session {SessionId} entered Error state after jeopardy timeout.", _chubby.SessionId);
            jeopardyCts?.Cancel();
        }
        catch (OperationCanceledException)
        {
            return;
        }
    }

    private void CompleteRecovery()
    {
        timerCts?.Cancel();
        timerCts?.Dispose();
        timerCts = null;

        jeopardyCts?.Cancel();
        jeopardyCts?.Dispose();
        jeopardyCts = null;
        _recoveryTask = null;

        _chubby.SessionState = SessionState.Normal;
        _logger.LogInformation("Client session {SessionId} returned to Normal state from jeopardy.", _chubby.SessionId);
    }

    private int GetTimeToWait(int timeOut)
    {
        return (int)TimeSpan.FromSeconds(Math.Max(timeOut - 5, 5)).TotalMilliseconds;
    }

    public void Dispose()
    {
        _logger.LogInformation("Disposing KeepAliveMonitor for session {SessionId}.", _chubby.SessionId);
        timerCts?.Cancel();
        timerCts?.Dispose();
        jeopardyCts?.Cancel();
        jeopardyCts?.Dispose();
        cts.Cancel();
        cts.Dispose();
        stream?.Dispose();
        _recoveryTask = null;
    }
}
