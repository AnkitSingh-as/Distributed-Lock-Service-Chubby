using Chubby.Core.Model;

namespace Chubby.Core.Events;

public class ConflictingLockRequestEvent : Event
{
    public int EventType => (int)ClientEventType.ConflictingLockRequest;
    public required string Path { get; init; }
    public long InstanceNumber { get; init; }
    public LockType LockType { get; init; }
    public required string HandleId { get; init; }
}
