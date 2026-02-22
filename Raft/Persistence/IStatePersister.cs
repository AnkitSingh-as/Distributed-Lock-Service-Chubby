namespace Raft;

public interface IStatePersister
{
    void Enqueue(PersistentCommand state);
    Task<PersistentState> LoadStateAsync();
}
