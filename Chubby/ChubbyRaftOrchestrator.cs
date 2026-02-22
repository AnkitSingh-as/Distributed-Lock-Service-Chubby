using Raft;
using Chubby.Core.Rpc;

public class ChubbyRaftOrchestrator
{
    private readonly NodeEnvelope _nodeEnvelope;
    private readonly ChubbyRpcProxy _chubbyRpcProxy;
    private volatile TaskCompletionSource<bool> _leaderReadyTcs;

    public ChubbyRaftOrchestrator(NodeEnvelope nodeEnvelope, ChubbyRpcProxy chubbyCore)
    {
        _nodeEnvelope = nodeEnvelope;
        _chubbyRpcProxy = chubbyCore;
        _leaderReadyTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _leaderReadyTcs.TrySetResult(true);
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
            _chubbyRpcProxy.StartSessionScheduler();
        }
        else
        {
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
}