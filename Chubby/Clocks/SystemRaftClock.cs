using Raft;
using System.Diagnostics;

namespace Chubby.Clocks;

public sealed class SystemRaftClock : IRaftClock
{
    public long Now => Stopwatch.GetTimestamp();

    public void Advance(TimeSpan timeSpan)
    {
        
    }
}
