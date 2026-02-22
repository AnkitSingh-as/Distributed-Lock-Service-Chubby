namespace Chubby.Core.Events;

public class MasterFailOverEvent : Event
{
    public int EventType => (int)ClientEventType.MasterFailOver;
    public int EpochNumber { get; init; }
}
