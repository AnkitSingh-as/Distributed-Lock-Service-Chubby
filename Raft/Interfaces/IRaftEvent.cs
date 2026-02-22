namespace Raft;

public interface IRaftEvent
{
    void Apply(RaftNode raftNode);
}