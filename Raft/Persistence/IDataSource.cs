namespace Raft;

public interface IDataSource
{
   public Task SaveStateAsync(PersistentState state);
   public Task<PersistentState> LoadStateAsync();
}