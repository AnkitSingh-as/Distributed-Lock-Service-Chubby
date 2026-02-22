using System.Threading.Channels;
using System.Collections.Concurrent;
using Chubby.Core.Model;
using Chubby.Core.Rpc;
using Chubby.Core.Events;

namespace Chubby.Core.Sessions;

public class Session
{
    public readonly Client client;
    private int leaseTimeout;
    public ConcurrentDictionary<string, Handle> Handles = new();
    public ConcurrentDictionary<string, Node> pathToEphemeralNodesMapping = new();
    public readonly string SessionId;
    public long LastActivityTicks { get; private set; }
    public Session(string sessionId, int leaseTimeout, Client client, int threshold = 1 * 1000)
    {
        this.SessionId = sessionId;
        this.leaseTimeout = leaseTimeout;
        this.client = client;
        this.threshold = threshold;
        UpdateLastActivity();
    }

    private bool _isWaitingForAcknowledgmentForFailoverEvent = false;

    public Channel<Event> channel { get; private set; } = Channel.CreateUnbounded<Event>();

    private readonly int threshold;

    // used to cancel all the pending tasks when session is closed. important.....
    public CancellationTokenSource cts = new CancellationTokenSource();

    public void UpdateLastActivity()
    {
        LastActivityTicks = DateTime.UtcNow.Ticks;
    }

    public bool IsWaitingForAcknowledgmentForFailoverEvent()
    {
        return _isWaitingForAcknowledgmentForFailoverEvent;
    }

    // refactor this to return keepAlive return type instead of object.
    public async Task<KeepAliveResponse> KeepAlive()
    {
        _isWaitingForAcknowledgmentForFailoverEvent = false;
        var waitToReadTask = channel.Reader.WaitToReadAsync(cts.Token).AsTask();
        var leaseTimeoutTask = Task.Delay(leaseTimeout - threshold, cts.Token);
        var completedTask = await Task.WhenAny(waitToReadTask, leaseTimeoutTask);

        // Cancel the other, non-completed task to prevent it from lingering.
        cts.Cancel();

        if (completedTask == leaseTimeoutTask)
        {
            // The lease timeout was reached.
            // We await the task to propagate any potential (though unlikely) exceptions.
            await leaseTimeoutTask;
            return new LeaseAboutToExpire { LeaseTimeout = leaseTimeout };
        }

        // Otherwise, an event on the channel woke us up.
        try
        {
            bool canRead = await waitToReadTask;
            if (canRead)
            {
                var events = new List<Event>();
                // I want to read all the items at once, present in this moment in this channel until no events available.
                while (!cts.IsCancellationRequested && channel.Reader.TryRead(out var ev))
                {
                    events.Add(ev);
                }
                if (events.FirstOrDefault() is MasterFailOverEvent)
                {
                    _isWaitingForAcknowledgmentForFailoverEvent = true;
                }

                return new EventAvailable { Events = events };
            }
            else
            {
                return new CacheInvalidation();
            }
        }
        catch (OperationCanceledException)
        {
            // This can happen in a rare race condition where the timeout task completes
            // and cancels this task before the `if (completedTask == leaseTimeoutTask)` check runs.
            // In this scenario, the timeout is the true cause.
            return new LeaseAboutToExpire { LeaseTimeout = leaseTimeout };
        }
        catch (Exception ex)
        {
            // Log the exception .
            throw;
        }
    }

    public void AddHandleToSession(Handle handle)
    {
        Handles.TryAdd(handle.id, handle);
    }

}

