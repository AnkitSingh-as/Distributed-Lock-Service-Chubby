using Chubby.Core.Model;

namespace Chubby.Core.Utils;

public static class NodeExtensions
{
    public static Stat GetStat(this Node node)
    {
        return new Stat
        {
            InstanceNumber = node.Instance,
            ContentGenerationNumber = node.ContentGenerationNumber,
            LockGenerationNumber = node.LockGenerationNumber,
            AclGenerationNumber = node.AclGenerationNumber,
            ContentLength = node.ContentLength
        };
    }
}
