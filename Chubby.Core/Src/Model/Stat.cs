namespace Chubby.Core.Model;

public class Stat
{
    public required long InstanceNumber { get; init; }
    public required long ContentGenerationNumber { get; init; }
    public required long LockGenerationNumber { get; init; }
    public required long AclGenerationNumber { get; init; }
    public required long ContentLength { get; init; }
}

public class ContentAndStat
{
    public required byte[] Content { get; init; }
    public required Stat Stat { get; init; }
}

