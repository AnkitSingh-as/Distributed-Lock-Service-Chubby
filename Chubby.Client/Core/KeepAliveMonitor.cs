using Chubby.Protos;
using Grpc.Core;

public class KeepAliveMonitor : IDisposable
{
    private readonly ChubbyClient _chubby;
    private readonly CancellationTokenSource cts;

    private CancellationTokenSource? timerCts;
    private CancellationTokenSource? jeopardyCts;
    private AsyncDuplexStreamingCall<KeepAliveRequest, KeepAliveResponse>? stream;
    private Task? _recoveryTask;

    public KeepAliveMonitor(ChubbyClient chubbyClient)
    {
        _chubby = chubbyClient;
        cts = new CancellationTokenSource();
    }

    public async Task Initialize()
    {
        // do I need this cts? 
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
        stream = _chubby.KeepAlive(null, linkedCts.Token);
        await Call(linkedCts.Token);
        await ReadResponse(linkedCts.Token);
    }

    public async Task Call(CancellationToken token)
    {
        if (stream is null || token.IsCancellationRequested)
        {
            return;
        }

        try
        {
            await stream.RequestStream.WriteAsync(new KeepAliveRequest
            {
                SessionId = _chubby.SessionId
            }, token);
        }
        catch (OperationCanceledException)
        {
            return;
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
                        var events = response.EventAvailable.Events.ToList();
                        _chubby.ProcessEvents(events);
                        if (events.Any(ev => ev.PayloadCase is Event.PayloadOneofCase.MasterFailOver))
                        {
                            await UndoJeopardyState();
                        }
                        await Call(token);
                        break;

                    case KeepAliveResponse.ReasonOneofCase.LeaseAboutToExpire:
                        timerCts?.Cancel();
                        timerCts?.Dispose();
                        timerCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
                        _ = StartTimer(response.LeaseAboutToExpire.LeaseTimeout, timerCts.Token);
                        await Call(token);
                        break;

                    default:
                        throw new NotSupportedException();
                }
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (RpcException)
        {
            await GoInJeopardyState(cts.Token);
        }
    }

    private async Task StartTimer(int masterLeaseTimeout, CancellationToken token)
    {
        try
        {
            await Task.Delay(GetTimeToWait(masterLeaseTimeout), token);
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

        _chubby.SessionState = SessionState.Jeopardy;
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
                await Initialize();
                return;
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
            jeopardyCts?.Cancel();
        }
        catch (OperationCanceledException)
        {
            return;
        }
    }

    public async Task UndoJeopardyState()
    {
        timerCts?.Cancel();
        timerCts?.Dispose();
        timerCts = null;

        jeopardyCts?.Cancel();
        jeopardyCts?.Dispose();
        jeopardyCts = null;

        if (_recoveryTask is not null)
        {
            try
            {
                await _recoveryTask;
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                _recoveryTask = null;
            }
        }

        _chubby.SessionState = SessionState.Normal;
    }

    private int GetTimeToWait(int timeOut)
    {
        return (int)TimeSpan.FromSeconds(Math.Max(timeOut - 5, 5)).TotalMilliseconds;
    }

    public void Dispose()
    {
        timerCts?.Cancel();
        timerCts?.Dispose();
        jeopardyCts?.Cancel();
        jeopardyCts?.Dispose();
        cts.Cancel();
        cts.Dispose();
        stream?.Dispose();
    }
}
