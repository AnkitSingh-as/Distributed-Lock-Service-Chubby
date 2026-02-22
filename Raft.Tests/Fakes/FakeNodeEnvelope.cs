using Raft.Tests.Fakes;
using System.Collections.Generic;

namespace Raft.Tests.Fakes
{
    public class FakeNodeEnvelope : INodeEnvelope
    {
        public IStatePersister StatePersister { get; }
        public ActivityTracker ActivityTracker { get; }

        public int SignalAllReplicatorsCalls { get; private set; }
        public List<int> SignaledReplicators { get; } = new List<int>();
        public int SignalAllVoteRequestersCalls { get; private set; }

        public FakeNodeEnvelope(IRaftClock clock)
        {
            StatePersister = new FakeStatePersister();
            ActivityTracker = new ActivityTracker(clock);
        }

        public void SignalAllReplicators()
        {
            SignalAllReplicatorsCalls++;
        }

        public void SignalReplicator(int followerId)
        {
            SignaledReplicators.Add(followerId);
        }

        public void SignalAllVoteRequesters()
        {
            SignalAllVoteRequestersCalls++;
        }

        public void AllEntriesCommitted()
        {

        }

    }
}
