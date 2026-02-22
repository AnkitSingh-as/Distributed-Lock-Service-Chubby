using Microsoft.Extensions.Logging;

namespace Raft;

public class HeartBeatTimer
{
    private RaftNode _node;
    private readonly ActivityTracker _clock;
    private readonly ILogger<HeartBeatTimer> _logger;


    public HeartBeatTimer(RaftNode node, ActivityTracker raftClock, ILogger<HeartBeatTimer> logger)
    {
        _node = node;
        _clock = raftClock;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct, int HeartBeatTimeout)
    {
        while (!ct.IsCancellationRequested)
        {
            _logger.LogTrace("Node {NodeId} heartbeat timer waiting for {HeartBeatTimeout} ms.", _node.Id, HeartBeatTimeout);
            try
            {
                await Task.Delay(HeartBeatTimeout, ct);
            }
            catch (TaskCanceledException)
            {
                _logger.LogDebug("Node {NodeId} heartbeat timer task cancelled", _node.Id);
                return;
            }
            _logger.LogDebug("Heartbeat Timeout triggered for Node {NodeId}", _node.Id);
            _node.enqueueEvent(new HeartBeatEvent());
        }

    }
}