using Proto = Chubby.Protos;

public sealed class ConflictingLockRequestEvent : IClientEvent
{
    public string Path { get; }
    public long InstanceNumber { get; }
    public Proto.LockType LockType { get; }
    public string HandleId { get; }

    public ConflictingLockRequestEvent(Proto.ConflictingLockRequestEvent @event)
    {
        Path = @event.Path;
        InstanceNumber = @event.InstanceNumber;
        LockType = @event.LockType;
        HandleId = @event.HandleId;
    }

    public void Apply(ClientCacheState state)
    {
        state.RecordConflictingLockRequest(HandleId, Path, InstanceNumber, LockType);
    }
}
