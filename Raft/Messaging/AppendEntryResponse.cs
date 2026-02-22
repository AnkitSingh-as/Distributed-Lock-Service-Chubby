namespace Raft;

public class AppendEntryResponse: Persistable
{
    /// <summary>
    /// CurrentTerm, for leader to update itself
    /// </summary>
    public int Term { get; set; }

    /// <summary>
    /// True if follower contained entry matching prevLogIndex and prevLogTerm
    /// </summary>
    public bool Success { get; set; }
}