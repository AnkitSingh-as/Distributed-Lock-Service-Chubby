using Chubby.Core.Model;

namespace Chubby.Core.Events;

public class LockAcquiredEvent : Event
{
    public int EventType => (int)ClientEventType.LockAcquired;
    public required string Path { get; init; }
    public required long InstanceNumber { get; init; }
    public required LockType LockType { get; init; }
    public required string HandleId { get; init; }
}
