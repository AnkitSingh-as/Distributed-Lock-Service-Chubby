using System.Threading;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Raft;

public class VoteRequester : IDisposable
{
    private readonly int _peerId;
    private readonly RaftNode _node;
    private readonly NodeEnvelope _nodeEnvelope;
    private readonly ILogger _logger;
    private readonly Channel<bool> _channel = Channel.CreateUnbounded<bool>(
        new UnboundedChannelOptions { SingleReader = true });

    private readonly CancellationTokenSource _cts = new();

    public VoteRequester(int peerId, RaftNode node, NodeEnvelope nodeEnvelope, ILoggerFactory loggerFactory)
    {
        _peerId = peerId;
        _node = node;
        _nodeEnvelope = nodeEnvelope;
        _logger = loggerFactory.CreateLogger($"{GetType().Name}-{_peerId}");
        _ = RunAsync(_cts.Token);
    }

    public void Stop()
    {
        _logger.LogDebug("Stopping.");
        _cts.Cancel();
        _channel.Writer.TryComplete();
    }

    public void Dispose()
    {
        _cts.Dispose();
    }

    public void Signal()
    {
        _logger.LogDebug("Signaled.");
        _channel.Writer.TryWrite(true);
    }

    private async Task RunAsync(CancellationToken token)
    {
        _logger.LogDebug("Starting vote request worker.");
        try
        {
            await foreach (var _ in _channel.Reader.ReadAllAsync(token))
            {
                _logger.LogTrace("Worker loop triggered.");
                await SendOnce();
            }
        }
        catch (OperationCanceledException)
        {
            // This is expected when stopping.
            _logger.LogDebug("Vote request worker was canceled.");
        }
        _logger.LogDebug("Vote request worker stopped.");
    }

    private async Task SendOnce()
    {
        // It's possible for the node's role to have changed since the election
        // was started. Only send a vote request if we are still a candidate.
        if (_node.CurrentRole != Role.Candidate)
        {
            _logger.LogDebug("Aborting vote request; node is no longer a candidate.");
            return;
        }

        var requestVoteArgs = _node.GetRequestVoteSnapshot();

        RequestVoteResponse reply;

        try
        {
            _logger.LogDebug("Sending RequestVote. Term: {Term}, LastLogIndex: {LastLogIndex}, LastLogTerm: {LastLogTerm}", requestVoteArgs.Term, requestVoteArgs.LastLogIndex, requestVoteArgs.LastLogTerm);
            reply = await _nodeEnvelope.RequestVoteAsync(_peerId, requestVoteArgs);
            _logger.LogDebug("Received RequestVote reply. VoteGranted: {VoteGranted}, Term: {Term}", reply.VoteGranted, reply.Term);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("RequestVote send was canceled.");
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send RequestVote RPC to Peer {PeerId}.", _peerId);
            return;
        }

        _logger.LogTrace("Enqueuing RequestVoteResponseEvent for Peer {PeerId}. VoteGranted: {VoteGranted}, Term: {Term}", _peerId, reply.VoteGranted, reply.Term);
        _node.enqueueEvent(
            new RequestVoteResponseEvent(
                _peerId,
                reply.Term,
                reply.VoteGranted
            )
        );
    }
}
