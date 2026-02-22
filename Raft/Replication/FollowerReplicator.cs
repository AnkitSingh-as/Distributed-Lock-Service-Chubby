using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Raft;

public sealed class FollowerReplicator : IDisposable
{
    private readonly int _followerId;
    private readonly RaftNode _node;
    private readonly NodeEnvelope _nodeEnvelope;
    private readonly ILogger _logger;

    private readonly Channel<bool> _signal =
        Channel.CreateUnbounded<bool>(
            new UnboundedChannelOptions { SingleReader = true });

    private readonly CancellationTokenSource _cts = new();

    public FollowerReplicator(
        int followerId,
        RaftNode node,
        NodeEnvelope nodeEnvelope,
        ILoggerFactory loggerFactory)
    {
        _followerId = followerId;
        _node = node;
        _nodeEnvelope = nodeEnvelope;
        _logger = loggerFactory.CreateLogger($"{GetType().Name}-{_followerId}");
        _ = RunAsync(_cts.Token);
    }

    public void Stop()
    {
        _logger.LogDebug("Stopping.");
        _cts.Cancel();
        _signal.Writer.TryComplete();
    }

    public void Dispose()
    {
        _cts.Dispose();
    }

    public void Signal()
    {
        _logger.LogDebug("Signaled.");
        // TryWrite is used to avoid blocking. For an unbounded channel, it will
        // only return false if the channel has been completed.
        _signal.Writer.TryWrite(true);
    }

    private async Task RunAsync(CancellationToken token)
    {
        _logger.LogDebug("Starting replication worker.");
        try
        {
            await foreach (var _ in _signal.Reader.ReadAllAsync(token))
            {

                _logger.LogTrace("Worker loop triggered.");
                await SendOnce();
            }
        }
        catch (OperationCanceledException)
        {
            // This is expected when stopping.
            _logger.LogDebug("Replication worker was canceled.");
        }
        _logger.LogDebug("Replication worker stopped.");
    }

    private async Task SendOnce()
    {
        // It's possible for the node's role to have changed since being signaled.
        // Only send AppendEntries if we are still the leader.
        if (_node.CurrentRole != Role.Leader)
        {
            _logger.LogDebug("Aborting replication; node is no longer a leader.");
            return;
        }

        var snap = _node.GetReplicationSnapshot(_followerId);

        _logger.LogTrace("Created replication snapshot for Follower {FollowerId}. PrevLogIndex: {PrevLogIndex}, PrevLogTerm: {PrevLogTerm}, EntryCount: {EntryCount}, LeaderCommit: {LeaderCommit}", _followerId, snap.PrevLogIndex, snap.PrevLogTerm, snap.Entries.Count, snap.LeaderCommit);

        int prevLogIndex = snap.PrevLogIndex;
        int entriesCount = snap.Entries.Count;

        var args = new AppendEntriesArgs
        {
            Term = snap.Term,
            LeaderId = snap.LeaderId,
            PrevLogIndex = prevLogIndex,
            PrevLogTerm = snap.PrevLogTerm,
            Entries = snap.Entries,
            LeaderCommit = snap.LeaderCommit
        };

        AppendEntryResponse reply;

        try
        {
            _logger.LogDebug("Sending AppendEntries. PrevLogIndex: {PrevLogIndex}, PrevLogTerm: {PrevLogTerm}, EntryCount: {EntryCount}", args.PrevLogIndex, args.PrevLogTerm, args.Entries.Count);
            reply = await _nodeEnvelope.AppendEntriesAsync(_followerId, args);
            _logger.LogDebug("Received AppendEntries reply. Success: {Success}, Term: {Term}", reply.Success, reply.Term);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("AppendEntries send was canceled.");
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send AppendEntries RPC to Follower {FollowerId}.", _followerId);
            return;
        }

        int newMatchIndex = reply.Success
            ? prevLogIndex + entriesCount
            : -1; // ignored on failure

        _logger.LogTrace("Enqueuing AppendEntriesResponseEvent for Follower {FollowerId}. Success: {Success}, Term: {Term}", _followerId, reply.Success, reply.Term);
        _node.enqueueEvent(
            new AppendEntriesResponseEvent(
                _followerId,
                reply.Term,
                reply.Success,
                newMatchIndex
            )
        );
    }

}
