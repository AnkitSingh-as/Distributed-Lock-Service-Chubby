namespace Raft;

public class RequestVoteEvent : RaftNodeEvent<RequestVoteResponse>
{
    private readonly int _term;
    private readonly int _candidateId;
    private readonly int _lastLogIndex;
    private readonly int _lastLogTerm;

    public RequestVoteEvent(
        int term,
        int candidateId,
        int lastLogIndex,
        int lastLogTerm) : base()
    {
        _term = term;
        _candidateId = candidateId;
        _lastLogIndex = lastLogIndex;
        _lastLogTerm = lastLogTerm;
    }

    protected override RequestVoteResponse Handle(RaftNode node)
    {
        return node.HandleRequestVote(_term, _candidateId, _lastLogIndex, _lastLogTerm);
    }
}