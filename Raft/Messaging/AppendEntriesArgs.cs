namespace Raft;

public sealed class AppendEntriesArgs
{
    public int Term { get; set; }
    public int LeaderId { get; set; }
    public int PrevLogIndex { get; set; }
    public int PrevLogTerm { get; set; }

    public List<Log> Entries { get; set; } = new List<Log>();
    public int LeaderCommit { get; set; }
}