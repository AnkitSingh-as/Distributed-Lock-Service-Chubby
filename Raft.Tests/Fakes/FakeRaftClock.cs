using System;
using System.Diagnostics;

namespace Raft.Tests.Fakes
{
    public class FakeRaftClock : IRaftClock
    {
        public long Now { get; set; }

        public void Advance(TimeSpan timeSpan)
        {
            Now += timeSpan.Ticks;
        }
    }
}
