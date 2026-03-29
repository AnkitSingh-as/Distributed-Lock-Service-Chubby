using Proto = Chubby.Protos;

public sealed class LockAcquiredEvent : IClientEvent
{
    public string Path { get; }
    public long InstanceNumber { get; }
    public Proto.LockType LockType { get; }
    public string HandleId { get; }

    public LockAcquiredEvent(Proto.LockAcquiredEvent @event)
    {
        Path = @event.Path;
        InstanceNumber = @event.InstanceNumber;
        LockType = @event.LockType;
        HandleId = @event.HandleId;
    }

    public void Apply(ClientCacheState state)
    {
        state.RecordLockAcquired(HandleId, Path, InstanceNumber, LockType);
    }
}
