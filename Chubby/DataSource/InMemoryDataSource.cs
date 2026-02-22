using Raft;
using System.Threading.Tasks;

namespace Chubby.DataSource;

public class InMemoryDataSource : IDataSource
{
    private PersistentState? _state;


    public Task<PersistentState> LoadStateAsync()
    {
        return Task.FromResult(_state ?? new PersistentState());
    }

    public Task SaveStateAsync(PersistentState state)
    {
        _state = state;
        return Task.CompletedTask;
    }
}