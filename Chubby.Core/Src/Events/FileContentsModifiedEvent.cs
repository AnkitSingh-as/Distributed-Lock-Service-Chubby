namespace Chubby.Core.Events;

public class FileContentsModifiedEvent : Event
{
    public int EventType => (int)ClientEventType.FileContentsModified;
    public required string Path { get; init; }
    public long InstanceNumber { get; init; }
    public long ContentGenerationNumber { get; init; }
}
