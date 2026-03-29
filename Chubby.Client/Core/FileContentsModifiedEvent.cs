using Proto = Chubby.Protos;

public sealed class FileContentsModifiedEvent : IClientEvent
{
    public string Path { get; }
    public long InstanceNumber { get; }
    public long ContentGenerationNumber { get; }
    public string HandleId { get; }

    public FileContentsModifiedEvent(Proto.FileContentsModifiedEvent @event)
    {
        Path = @event.Path;
        InstanceNumber = @event.InstanceNumber;
        ContentGenerationNumber = @event.ContentGenerationNumber;
        HandleId = @event.HandleId;
    }

    public void Apply(ClientCacheState state)
    {
        state.InvalidateContents(Path, InstanceNumber, ContentGenerationNumber, HandleId);
    }
}
