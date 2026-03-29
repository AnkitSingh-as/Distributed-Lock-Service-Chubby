public class Node
{
    public required string Path { get; init; }
    public required byte[] Content { get; init; }
    public required Stat Stat { get; init; }
}

public class Stat
{
    public required long InstanceNumber { get; init; }
    public required long ContentGenerationNumber { get; init; }
    public required long LockGenerationNumber { get; init; }
    public required long AclGenerationNumber { get; init; }
    public required long ContentLength { get; init; }
}