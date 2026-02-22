using Microsoft.Extensions.Logging;

namespace Raft;

public sealed class ElectionTimer
{
    private readonly RaftNode _node;
    private readonly ActivityTracker _clock;
    private readonly ILogger<ElectionTimer> _logger;

    private readonly Random _rng = new();

    public ElectionTimer(RaftNode node, ActivityTracker clock, ILogger<ElectionTimer> logger)
    {
        _node = node;
        _clock = clock;
        _logger = logger;
    }

    public async Task RunAsync(
        CancellationToken ct,
        int minTimeoutMs,
        int maxTimeoutMs)
    {
        while (!ct.IsCancellationRequested)
        {
            var timeoutMs = _rng.Next(minTimeoutMs, maxTimeoutMs);
            _logger.LogTrace("Node {NodeId} election timer set for {TimeoutMs} ms.", _node.Id, timeoutMs);

            var observedActivity = _clock.LastActivityTicks;
            try
            {
                await Task.Delay(timeoutMs, ct);
            }
            catch (TaskCanceledException)
            {
                _logger.LogDebug("Node {NodeId} election timer task cancelled", _node.Id);
                return;
            }
            _logger.LogDebug("Node {NodeId} going to check for election timeout for values lastActivityTicks: {LastActivityTicks}, observedActivity: {ObservedActivity}", _node.Id, _clock.LastActivityTicks, observedActivity);
            if (_clock.LastActivityTicks == observedActivity)
            {
                _logger.LogDebug("Election Timeout triggered for Node {NodeId}", _node.Id);
                _node.enqueueEvent(new ElectionTimeoutEvent());
            }
            else
            {
                _logger.LogTrace("Node {NodeId} election timeout averted due to recent activity.", _node.Id);
            }
        }
    }
}
