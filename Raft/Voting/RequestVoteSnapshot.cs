namespace Raft;

public class RequestVoteSnapshot
{
    public int Term { get; set; }
    public int CandidateId { get; set; }
    public int LastLogIndex { get; set; }
    public int LastLogTerm { get; set; }
}
