using System.Threading.Channels;
using System.Collections.Concurrent;
using Chubby.Core.Model;
using Chubby.Core.Rpc;
using Chubby.Core.Events;
using Microsoft.Extensions.Logging;

namespace Chubby.Core.Sessions;

public class Session
{
    public readonly Client client;
    private readonly int leaseTimeout;
    private readonly int threshold;
    private readonly object _ackSync = new();
    private long _nextSequenceNumber;
    private long _lastAcknowledgedSequenceNumber;
    private long? _pendingFailoverAcknowledgmentSequence;
    private readonly ConcurrentDictionary<string, long> _pathToContentsModificationAcknowledgment = new(StringComparer.Ordinal);
    private readonly ILogger<Session> _logger;

    public ConcurrentDictionary<string, Handle> Handles = new();
    public ConcurrentDictionary<string, Node> pathToEphemeralNodesMapping = new();
    public readonly string SessionId;
    public long LastActivityTicks { get; private set; }

    public Session(string sessionId, int leaseTimeout, Client client, ILogger<Session> logger, int threshold = 1 * 1000)
    {
        SessionId = sessionId;
        this.leaseTimeout = leaseTimeout;
        this.client = client;
        _logger = logger;
        this.threshold = threshold;
        UpdateLastActivity();
        _logger.LogInformation(
            "Session {SessionId} created for client {ClientName} with lease timeout {LeaseTimeout} ms and threshold {Threshold} ms.",
            SessionId,
            client.Name,
            leaseTimeout,
            threshold);
    }

    public Channel<DeliveredSessionEvent> channel { get; } = Channel.CreateUnbounded<DeliveredSessionEvent>();

    // used to cancel all the pending tasks when session is closed.
    public CancellationTokenSource cts = new CancellationTokenSource();

    public void UpdateLastActivity()
    {
        LastActivityTicks = DateTime.UtcNow.Ticks;
        _logger.LogDebug("Session {SessionId} activity updated.", SessionId);
    }

    public bool IsWaitingForAcknowledgmentForFailoverEvent()
    {
        lock (_ackSync)
        {
            return _pendingFailoverAcknowledgmentSequence.HasValue;
        }
    }

    public bool IsWaitingForContentsModifiedAcknowledgmentEvent(string path)
    {
        lock (_ackSync)
        {
            return _pathToContentsModificationAcknowledgment.TryGetValue(path, out var sequenceNumber)
                && sequenceNumber > _lastAcknowledgedSequenceNumber;
        }
    }

    public bool IsWaitingForAnyContentsModifiedAcknowledgmentEvent()
    {
        lock (_ackSync)
        {
            return _pathToContentsModificationAcknowledgment.Values.Any(sequenceNumber => sequenceNumber > _lastAcknowledgedSequenceNumber);
        }
    }

    public async Task<long> EnqueueEventAsync(Event @event, CancellationToken cancellationToken = default)
    {
        var deliveredEvent = new DeliveredSessionEvent
        {
            SequenceNumber = Interlocked.Increment(ref _nextSequenceNumber),
            Event = @event
        };

        await channel.Writer.WriteAsync(deliveredEvent, cancellationToken);
        _logger.LogInformation(
            "Enqueued {EventType} for session {SessionId} at sequence {SequenceNumber}.",
            deliveredEvent.Event.GetType().Name,
            SessionId,
            deliveredEvent.SequenceNumber);
        return deliveredEvent.SequenceNumber;
    }

    public async Task<KeepAliveResponse> KeepAlive(long ackedThroughSequence, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "KeepAlive received for session {SessionId} with ackedThroughSequence {AckedThroughSequence}.",
            SessionId,
            ackedThroughSequence);
        AcknowledgeThrough(ackedThroughSequence);

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, cancellationToken);
        var waitToReadTask = channel.Reader.WaitToReadAsync(linkedCts.Token).AsTask();
        var leaseTimeoutTask = Task.Delay(leaseTimeout - threshold, linkedCts.Token);
        var completedTask = await Task.WhenAny(waitToReadTask, leaseTimeoutTask);

        linkedCts.Cancel();

        if (completedTask == leaseTimeoutTask)
        {
            try
            {
                await leaseTimeoutTask;
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                throw;
            }

            _logger.LogDebug(
                "KeepAlive for session {SessionId} is returning LeaseAboutToExpire with lease timeout {LeaseTimeout}.",
                SessionId,
                leaseTimeout);
            return new LeaseAboutToExpire { LeaseTimeout = leaseTimeout };
        }

        try
        {
            await waitToReadTask;

            var events = new List<DeliveredSessionEvent>();
            while (channel.Reader.TryRead(out var deliveredEvent))
            {
                events.Add(deliveredEvent);
            }

            RecordPendingAcknowledgments(events);
            _logger.LogInformation(
                "KeepAlive for session {SessionId} is returning {EventCount} event(s). Highest sequence in batch: {HighestSequence}.",
                SessionId,
                events.Count,
                events.Count == 0 ? 0 : events.Max(x => x.SequenceNumber));
            return new EventAvailable { Events = events };
        }
        catch (OperationCanceledException) when (!cts.IsCancellationRequested)
        {
            _logger.LogDebug(
                "KeepAlive wait for session {SessionId} was canceled before shutdown; returning LeaseAboutToExpire.",
                SessionId);
            return new LeaseAboutToExpire { LeaseTimeout = leaseTimeout };
        }
    }

    public void AddHandleToSession(Handle handle)
    {
        Handles.TryAdd(handle.id, handle);
    }

    private void AcknowledgeThrough(long ackedThroughSequence)
    {
        if (ackedThroughSequence <= 0)
        {
            return;
        }

        lock (_ackSync)
        {
            if (ackedThroughSequence <= _lastAcknowledgedSequenceNumber)
            {
                return;
            }

            _lastAcknowledgedSequenceNumber = ackedThroughSequence;
            _logger.LogInformation(
                "Session {SessionId} acknowledged through sequence {AckedThroughSequence}.",
                SessionId,
                ackedThroughSequence);

            if (_pendingFailoverAcknowledgmentSequence is long failoverSequence
                && ackedThroughSequence >= failoverSequence)
            {
                _pendingFailoverAcknowledgmentSequence = null;
                _logger.LogInformation(
                    "Session {SessionId} cleared pending failover acknowledgment at sequence {SequenceNumber}.",
                    SessionId,
                    failoverSequence);
            }

            foreach (var pendingPath in _pathToContentsModificationAcknowledgment.ToArray())
            {
                if (ackedThroughSequence >= pendingPath.Value)
                {
                    _pathToContentsModificationAcknowledgment.TryRemove(pendingPath.Key, out _);
                    _logger.LogDebug(
                        "Session {SessionId} acknowledged pending content modification for path {Path} at sequence {SequenceNumber}.",
                        SessionId,
                        pendingPath.Key,
                        pendingPath.Value);
                }
            }
        }
    }

    private void RecordPendingAcknowledgments(IEnumerable<DeliveredSessionEvent> deliveredEvents)
    {
        lock (_ackSync)
        {
            foreach (var deliveredEvent in deliveredEvents)
            {
                switch (deliveredEvent.Event)
                {
                    case MasterFailOverEvent:
                        _pendingFailoverAcknowledgmentSequence = deliveredEvent.SequenceNumber;
                        _logger.LogWarning(
                            "Session {SessionId} now waits for failover acknowledgment at sequence {SequenceNumber}.",
                            SessionId,
                            deliveredEvent.SequenceNumber);
                        break;

                    case FileContentsModifiedEvent fileContentsModifiedEvent:
                        _pathToContentsModificationAcknowledgment[fileContentsModifiedEvent.Path] = deliveredEvent.SequenceNumber;
                        _logger.LogDebug(
                            "Session {SessionId} now waits for content modification acknowledgment for path {Path} at sequence {SequenceNumber}.",
                            SessionId,
                            fileContentsModifiedEvent.Path,
                            deliveredEvent.SequenceNumber);
                        break;
                }
            }
        }
    }
}
