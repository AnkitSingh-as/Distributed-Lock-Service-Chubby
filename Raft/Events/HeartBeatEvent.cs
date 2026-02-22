namespace Raft;

public class HeartBeatEvent : IRaftEvent
{
    public void Apply(RaftNode raftNode)
    {
        raftNode.HandleHeartBeat();
    }
}