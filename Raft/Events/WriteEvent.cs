namespace Raft;

public class WriteEvent : IRaftEvent
{
    private readonly byte[] _command;
    private readonly TaskCompletionSource<object?> Ack;

    public WriteEvent(byte[] command, TaskCompletionSource<object?> tcs)
    {
        _command = command;
        Ack = tcs;
    }

    public void Apply(RaftNode node)
    {
        node.HandleClientWrite(_command, Ack);
    }
}
