using Raft;
using Chubby.Core.Rpc;

public class ChubbyRaftOrchestrator
{
    private readonly INodeEnvelope _nodeEnvelope;
    private readonly ChubbyRpcProxy _chubbyRpcProxy;
    private volatile TaskCompletionSource<bool> _leaderReadyTcs;
    private volatile CancellationTokenSource _leaderLifetimeCts;

    public ChubbyRaftOrchestrator(INodeEnvelope nodeEnvelope, ChubbyRpcProxy chubbyCore)
    {
        _nodeEnvelope = nodeEnvelope;
        _chubbyRpcProxy = chubbyCore;
        _leaderReadyTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _leaderReadyTcs.TrySetResult(true);
        _leaderLifetimeCts = new CancellationTokenSource();
        _leaderLifetimeCts.Cancel();
        SubscribeToRaftEvents();
    }

    public void SubscribeToRaftEvents()
    {
        _nodeEnvelope.RoleChanged += OnRoleChanged;
        _nodeEnvelope.AllEntriesCommittedOnTransitionToLeader += OnRaftLeaderReady;
    }

    private void OnRoleChanged(Role oldRole, Role newRole)
    {
        if (newRole == Role.Leader && oldRole != Role.Leader)
        {
            Interlocked.Exchange(ref _leaderReadyTcs, new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously));
            var previousCts = Interlocked.Exchange(ref _leaderLifetimeCts, new CancellationTokenSource());
            previousCts.Dispose();
            _chubbyRpcProxy.StartSessionScheduler();
        }
        else
        {
            var previousCts = Interlocked.Exchange(ref _leaderLifetimeCts, new CancellationTokenSource());
            previousCts.Cancel();
            previousCts.Dispose();
            _leaderLifetimeCts.Cancel();
            _chubbyRpcProxy.StopSessionScheduler();
        }
    }

    private void OnRaftLeaderReady()
    {
        _ = PrepareChubbyLeaderAsync();
    }

    private async Task PrepareChubbyLeaderAsync()
    {
        await _chubbyRpcProxy.ProcessFailOver();
        _leaderReadyTcs.TrySetResult(true);
    }

    internal Task GetTaskToAwaitBeforeProceeding()
    {
        return _leaderReadyTcs.Task;
    }

    internal CancellationToken GetLeaderLifetimeToken()
    {
        return _leaderLifetimeCts.Token;
    }
}
