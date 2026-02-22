namespace Raft;

public sealed class AppendEntriesResponseEvent : IRaftEvent
{
    private readonly int _followerId;
    private readonly int _term;
    private readonly bool _success;
    private readonly int _matchIndex;

    public AppendEntriesResponseEvent(
        int followerId,
        int term,
        bool success,
        int matchIndex)
    {
        _followerId = followerId;
        _term = term;
        _success = success;
        _matchIndex = matchIndex;
    }

    public void Apply(RaftNode node)
    {
        node.HandleAppendResponse(
            _followerId,
            _term,
            _success,
            _matchIndex
        );
    }
}
