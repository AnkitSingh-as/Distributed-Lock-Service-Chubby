namespace Raft;

public sealed class LogPersistedEvent : IRaftEvent
{
    private readonly int _index;

    public LogPersistedEvent(int index)
    {
        _index = index;
    }

    public void Apply(RaftNode node)
    {
        node.MarkDurable(_index);
    }
}
