namespace Raft.Tests.Fakes
{
    public class FakeStatePersister : IStatePersister
    {
        public List<PersistentCommand> EnqueuedCommands { get; } = new List<PersistentCommand>();

        public void Enqueue(PersistentCommand state)
        {
            EnqueuedCommands.Add(state);
        }

        public Task<PersistentState> LoadStateAsync()
        {
            return Task.FromResult(new PersistentState());
        }
    }
}
