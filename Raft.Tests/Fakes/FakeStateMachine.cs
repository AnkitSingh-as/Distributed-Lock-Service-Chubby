using Raft;

public class FakeStateMachine : IStateMachine
{
    public object? Apply(byte[] command)
    {
        return null;
    }
}