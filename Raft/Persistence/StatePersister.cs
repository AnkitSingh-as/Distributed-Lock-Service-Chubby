using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Raft;

public class StatePersister : IStatePersister
{
    private readonly IDataSource _dataSource;
    private readonly ILogger<StatePersister> _logger;

    private readonly NodeEnvelope nodeEnvelope;
    private Channel<PersistentCommand> _eventChannel = Channel.CreateUnbounded<PersistentCommand>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
        }
    );

    public StatePersister(IDataSource dataSource, NodeEnvelope nodeEnvelope, ILogger<StatePersister> logger)
    {
        _dataSource = dataSource;
        this.nodeEnvelope = nodeEnvelope;
        _logger = logger;
        _ = StartEventLoopAsync();
    }

    public void Enqueue(PersistentCommand state)
    {
        _logger.LogDebug("Enqueuing state for persistence. Term: {Term}, VotedFor: {VotedFor}, Log Count: {LogCount}", state.PersistentState.CurrentTerm, state.PersistentState.VotedFor, state.PersistentState.Logs.Count);
        if (!_eventChannel.Writer.TryWrite(state))
        {
            _logger.LogError("Failed to write persistence command to channel.");
            throw new Exception("Failed to write event to channel.");
        }
    }

    public async Task StartEventLoopAsync()
    {
        _logger.LogDebug("Starting persistence event loop.");
        await foreach (var ev in _eventChannel.Reader.ReadAllAsync())
        {
            _logger.LogDebug("Persisting state. Term: {Term}, VotedFor: {VotedFor}, Log Count: {LogCount}", ev.PersistentState.CurrentTerm, ev.PersistentState.VotedFor, ev.PersistentState.Logs.Count);
            await _dataSource.SaveStateAsync(ev.PersistentState);
            _logger.LogDebug("State persisted successfully.");

            if (ev.DurableAck is not null)
            {
                _logger.LogTrace("Setting durable ACK for persisted state.");
                ev.DurableAck.SetResult();
            }
            foreach (var followUp in ev.FollowUpEvents)
            {
                _logger.LogDebug("Enqueuing follow-up event: {EventType}", followUp.GetType().Name);
                nodeEnvelope.Node.enqueueEvent(followUp);
            }
        }
        _logger.LogDebug("Persistence event loop stopped.");
    }

    public async Task<PersistentState> LoadStateAsync()
    {
        _logger.LogDebug("Loading state from data source.");
        var state = await _dataSource.LoadStateAsync();
        _logger.LogDebug("Loaded state. Term: {Term}, VotedFor: {VotedFor}, Log Count: {LogCount}", state.CurrentTerm, state.VotedFor, state.Logs.Count);
        return state;
    }
}
