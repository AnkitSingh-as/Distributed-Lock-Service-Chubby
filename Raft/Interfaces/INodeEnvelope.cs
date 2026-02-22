namespace Raft;

public interface INodeEnvelope
{
    IStatePersister StatePersister { get; }
    ActivityTracker ActivityTracker { get; }
    void SignalAllReplicators();
    void SignalReplicator(int followerId);
    void SignalAllVoteRequesters();

    void AllEntriesCommitted();
}
