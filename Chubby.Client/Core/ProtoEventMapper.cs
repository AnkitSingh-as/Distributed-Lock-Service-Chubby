using Chubby.Protos;

public static class ProtoEventMapper
{
    public static IClientEvent Map(Event @event) =>
        @event.PayloadCase switch
        {
            Event.PayloadOneofCase.FileContentsModified => new FileContentsModifiedEvent(@event.FileContentsModified),
            Event.PayloadOneofCase.InvalidHandleAndLock => new InvalidHandleAndLockEvent(@event.InvalidHandleAndLock),
            Event.PayloadOneofCase.ConflictingLockRequest => new ConflictingLockRequestEvent(@event.ConflictingLockRequest),
            Event.PayloadOneofCase.LockAcquired => new LockAcquiredEvent(@event.LockAcquired),
            Event.PayloadOneofCase.MasterFailOver => new MasterFailOverEvent(@event.MasterFailOver),
            _ => throw new NotSupportedException($"Unsupported event payload '{@event.PayloadCase}'.")
        };
}
