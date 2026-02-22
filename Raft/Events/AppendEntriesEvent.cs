using System.Diagnostics;

namespace Raft;

public class AppendEntriesEvent : RaftNodeEvent<AppendEntryResponse>
{
    private readonly int _term;
    private readonly int _leaderId;
    private readonly int _prevLogIndex;
    private readonly int _prevLogTerm;
    private readonly List<Log> _entries;
    private readonly int _leaderCommit;

    public AppendEntriesEvent(
        int term,
        int leaderId,
        int prevLogIndex,
        int prevLogTerm,
        List<Log> entries,
        int leaderCommit) : base()
    {
        _term = term;
        _leaderId = leaderId;
        _prevLogIndex = prevLogIndex;
        _prevLogTerm = prevLogTerm;
        _entries = entries;
        _leaderCommit = leaderCommit;
    }

    protected override AppendEntryResponse Handle(RaftNode node)
    {
        return node.HandleAppendEntries(_term, _leaderId, _prevLogIndex, _prevLogTerm, _entries, _leaderCommit);
    }
}