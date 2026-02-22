namespace Raft;

public class Persistable
{
    public TaskCompletionSource? Ack { get; private set; }

    /// <summary>
    /// Initializes the Ack TaskCompletionSource with TaskCreationOptions.RunContinuationsAsynchronously.
    /// </summary>

    public void WithAck()
    {
        Ack ??= RaftTcs.Create();
    }
}