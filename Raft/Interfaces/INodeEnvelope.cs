
namespace Raft;

public interface INodeEnvelope
{
    IStatePersister StatePersister { get; }
    ActivityTracker ActivityTracker { get; }
    public event Action<Role, Role>? RoleChanged;
    public event Action? AllEntriesCommittedOnTransitionToLeader;
    void SignalAllReplicators();
    void SignalReplicator(int followerId);
    void SignalAllVoteRequesters();
    void AllEntriesCommitted();
    int CurrentEpochNumber();
    bool IsLeader();
    Task<object?> WriteAsync(byte[] command);
}
