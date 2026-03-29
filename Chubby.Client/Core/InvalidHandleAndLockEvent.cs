using Proto = Chubby.Protos;

public sealed class InvalidHandleAndLockEvent : IClientEvent
{
    public string HandleId { get; }

    public InvalidHandleAndLockEvent(Proto.InvalidHandleAndLockEvent @event)
    {
        HandleId = @event.HandleId;
    }

    public void Apply(ClientCacheState state)
    {
        state.InvalidateHandleAndLock(HandleId);
    }
}
