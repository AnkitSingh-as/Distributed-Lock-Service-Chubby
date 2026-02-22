namespace Raft;

public class RequestVoteResponseEvent : IRaftEvent
{
    private int peerId;
    private int term;
    private bool voteGranted;

    public RequestVoteResponseEvent(int peerId, int term, bool voteGranted)
    {
        this.peerId = peerId;
        this.term = term;
        this.voteGranted = voteGranted;
    }

    public void Apply(RaftNode raftNode)
    {
        raftNode.HandleRequestVoteResponse(peerId, term, voteGranted);
    }
}