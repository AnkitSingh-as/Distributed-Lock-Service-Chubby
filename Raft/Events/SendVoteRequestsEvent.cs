namespace Raft;

/// <summary>
/// This event is enqueued after a node becomes a candidate and its state has been persisted.
/// Its purpose is to trigger the sending of RequestVote RPCs.
/// </summary>
public class SendVoteRequestsEvent : IRaftEvent
{
    private int term;

    public SendVoteRequestsEvent(int term)
    {
        this.term = term;
    }

    public void Apply(RaftNode raftNode)
    {
        // It's possible the node has stepped down to follower while waiting for persistence.
        // Only send vote requests if we are still a candidate.
        if (raftNode.CurrentRole == Role.Candidate && raftNode.State.CurrentTerm == term)
            raftNode.SendVoteRequests();
    }
}