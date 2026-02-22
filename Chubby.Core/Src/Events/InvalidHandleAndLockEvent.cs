namespace Chubby.Core.Events;

public class InvalidHandleAndLockEvent : Event
{
    public int EventType => (int)ClientEventType.InvalidHandleAndLock;
    public required string HandleId { get; init; }
}
