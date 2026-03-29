using Chubby.Core.Rpc;
using Chubby.Core.StateMachine;

namespace Chubby.Core.Sessions;

public class SessionScheduler : IDisposable
{
    private readonly SortedSet<SessionExpiryEntry> _set = new(new SessionExpiryComparer());
    private readonly Dictionary<string, SessionExpiryEntry> _entriesBySessionId = new(StringComparer.Ordinal);
    private readonly object _lock = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly AutoResetEvent _signal = new(false);
    private readonly ChubbyConfig _config;
    private readonly ChubbyRpcProxy _proxy;
    private Task? _schedulerTask;
    private int _started;
    public SessionScheduler(ChubbyCore chubby, ChubbyRpcProxy proxy, ChubbyConfig config)
    {
        _proxy = proxy;
        _config = config;
        chubby.SessionClosed += OnSessionClosed;
    }

    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) == 1)
        {
            return;
        }

        _schedulerTask = Task.Run(RunSchedulerLoop, _cts.Token);
    }

    private async Task RunSchedulerLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            string? sessionIdToExpire = null;
            int waitTimeout = Timeout.Infinite;

            lock (_lock)
            {
                if (_set.Min is SessionExpiryEntry nextEntry)
                {
                    sessionIdToExpire = nextEntry.SessionId;
                    var timeUntilExpiry = TimeSpan.FromTicks(nextEntry.ExpiryTicks - DateTime.UtcNow.Ticks);
                    waitTimeout = timeUntilExpiry > TimeSpan.Zero ? (int)timeUntilExpiry.TotalMilliseconds : 0;
                }
            }

            if (waitTimeout == 0 && sessionIdToExpire != null)
            {
                Console.WriteLine($"Session {sessionIdToExpire} expired. Proposing closure via Raft command, which will notify the scheduler on completion to clean this session");
                await _proxy.CloseSession(sessionIdToExpire);
            }
            else
            {
                _signal.WaitOne(waitTimeout);
            }
        }
    }

    private void OnSessionClosed(Session session)
    {
        Remove(session.SessionId);
    }

    // on handle closed add to tracking to expiry.
    public void AddOrUpdate(Session session)
    {
        var entry = CreateEntry(session);
        lock (_lock)
        {
            if (_entriesBySessionId.TryGetValue(session.SessionId, out var existingEntry))
            {
                _set.Remove(existingEntry);
            }

            _entriesBySessionId[session.SessionId] = entry;
            _set.Add(entry);
        }
        _signal.Set();
    }

    public void AddOrUpdateBulk(IEnumerable<Session> sessions)
    {
        lock (_lock)
        {
            foreach (var session in sessions)
            {
                var entry = CreateEntry(session);
                if (_entriesBySessionId.TryGetValue(session.SessionId, out var existingEntry))
                {
                    _set.Remove(existingEntry);
                }

                _entriesBySessionId[session.SessionId] = entry;
                _set.Add(entry);
            }
        }
        _signal.Set();
    }


    public void Remove(Session session)
    {
        Remove(session.SessionId);
    }

    public void Remove(string sessionId)
    {
        lock (_lock)
        {
            if (_entriesBySessionId.TryGetValue(sessionId, out var entry))
            {
                _set.Remove(entry);
                _entriesBySessionId.Remove(sessionId);
            }
        }
    }

    public void Stop()
    {
        // maybe the leader is not able to communicate with others, but its in memory state is intact, so clear the set for safety purposes.
        lock (_lock)
        {
            _set.Clear();
            _entriesBySessionId.Clear();
            _cts.Cancel();
            _signal.Set();
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _signal.Set();
        _cts.Dispose();
        _signal.Dispose();
    }

    private SessionExpiryEntry CreateEntry(Session session)
    {
        var expiryTicks = new DateTime(session.LastActivityTicks, DateTimeKind.Utc)
            .AddMilliseconds(_config.SessionExpiryTimeout)
            .Ticks;
        return new SessionExpiryEntry(session.SessionId, expiryTicks);
    }
}
