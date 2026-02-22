using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Chubby.Core.Rpc;
using Chubby.Core.StateMachine;

namespace Chubby.Core.Sessions;

public class SessionScheduler : IDisposable
{
    private readonly SortedSet<Session> _set = new(new SessionExpiryComparer());
    private readonly object _lock = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly AutoResetEvent _signal = new(false);
    private readonly ChubbyConfig _config;
    private readonly ChubbyRpcProxy _proxy;
    public SessionScheduler(ChubbyCore chubby, ChubbyRpcProxy proxy, ChubbyConfig config)
    {
        _proxy = proxy;
        _config = config;
        chubby.SessionClosed += OnSessionClosed;
    }

    public void Start()
    {
        Task.Factory.StartNew(RunSchedulerLoop, _cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    private async Task RunSchedulerLoop()
    {
        while (!_cts.IsCancellationRequested)
        {

            Session? sessionToExpire = null;
            int waitTimeout = Timeout.Infinite;

            lock (_lock)
            {
                if (_set.Min is Session nextSession)
                {
                    sessionToExpire = nextSession;
                    var expiryTime = new DateTime(nextSession.LastActivityTicks, DateTimeKind.Utc).AddMilliseconds(_config.SessionExpiryTimeout);
                    var timeUntilExpiry = expiryTime - DateTime.UtcNow;
                    waitTimeout = timeUntilExpiry > TimeSpan.Zero ? (int)timeUntilExpiry.TotalMilliseconds : 0;
                }
            }

            if (waitTimeout == 0 && sessionToExpire != null)
            {
                Console.WriteLine($"Session {sessionToExpire.SessionId} expired. Proposing closure via Raft command...");
                await _proxy.CloseSession(sessionToExpire.SessionId);
            }
            else
            {
                _signal.WaitOne(waitTimeout);
            }
        }
    }

    private void OnSessionClosed(Session session)
    {
        Remove(session);
    }

    // on handle closed add to tracking to expiry.
    public void AddOrUpdate(Session session)
    {
        lock (_lock)
        {
            _set.Remove(session);
            _set.Add(session);
        }
        _signal.Set();
    }

    public void AddOrUpdateBulk(IEnumerable<Session> sessions)
    {
        lock (_lock)
        {
            foreach (var session in sessions)
            {
                _set.Remove(session);
                _set.Add(session);
            }
        }
        _signal.Set();
    }


    public void Remove(Session session)
    {
        lock (_lock)
        {
            _set.Remove(session);
        }
    }

    public void Stop()
    {
        // maybe the leader is not able to communicate with others, but its in memory state is intact, so clear the set for safety purposes.
        _set.Clear();
        _cts.Cancel();
        _signal.Set();
    }

    public void Dispose()
    {
        _cts.Cancel();
        _signal.Set();
        _cts.Dispose();
        _signal.Dispose();
    }
}
