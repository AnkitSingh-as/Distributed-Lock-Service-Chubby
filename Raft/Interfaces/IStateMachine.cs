namespace Raft
{
    public interface IStateMachine
    {
        object? Apply(byte[] command);
    }
}