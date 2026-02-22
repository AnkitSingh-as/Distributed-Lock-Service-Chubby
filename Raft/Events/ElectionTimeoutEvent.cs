namespace Raft;

public class ElectionTimeoutEvent : IRaftEvent
{
    public void Apply(RaftNode raftNode)
    {
        raftNode.StartElection();
    }
}