using Proto = Chubby.Protos;

public sealed class MasterFailOverEvent : IClientEvent
{
    public int EpochNumber { get; }

    public MasterFailOverEvent(Proto.MasterFailOverEvent @event)
    {
        EpochNumber = @event.EpochNumber;
    }

    public void Apply(ClientCacheState state)
    {
        state.InvalidateAllForFailover(EpochNumber);
    }
}
