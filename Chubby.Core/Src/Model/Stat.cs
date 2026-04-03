namespace Chubby.Core.Model;

public abstract class Cacheable
{
    public bool IsCacheable { get; set; } = false;
}

public class Stat : Cacheable
{
    public required long InstanceNumber { get; init; }
    public required long ContentGenerationNumber { get; init; }
    public required long LockGenerationNumber { get; init; }
    public required long AclGenerationNumber { get; init; }
    public required long ContentLength { get; init; }
}

public class ContentAndStat : Cacheable
{
    public required byte[] Content { get; init; }
    public required Stat Stat { get; init; }
}

