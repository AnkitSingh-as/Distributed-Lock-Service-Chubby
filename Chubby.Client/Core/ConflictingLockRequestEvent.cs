using Proto = Chubby.Protos;

public sealed class ConflictingLockRequestEvent : IClientEvent
{
    public string Path { get; }
    public long InstanceNumber { get; }
    public Proto.LockType LockType { get; }

    public ConflictingLockRequestEvent(Proto.ConflictingLockRequestEvent @event)
    {
        Path = @event.Path;
        InstanceNumber = @event.InstanceNumber;
        LockType = @event.LockType;
    }

    public void Apply(ClientCacheState state)
    {
        state.RecordConflictingLockRequest( Path, InstanceNumber, LockType);
    }
}
