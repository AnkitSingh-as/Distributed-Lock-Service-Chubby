namespace Raft;

public class RequestVoteResponse : Persistable
{
    /// <summary>
    /// CurrentTerm, for candidate to update itself
    /// </summary>
    public int Term { get; set; }

    /// <summary>
    /// True means candidate received vote
    /// </summary>
    public bool VoteGranted { get; set; }
}