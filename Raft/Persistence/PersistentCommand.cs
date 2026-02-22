namespace Raft;

public class PersistentCommand
{
    public TaskCompletionSource? DurableAck { get; }

    public PersistentState PersistentState { get; }
    public IReadOnlyList<IRaftEvent> FollowUpEvents { get; }
    public PersistentCommand(PersistentState persistentState, TaskCompletionSource? taskCompletionSource = null, IReadOnlyList<IRaftEvent>? followUpEvents = null)
    {
        this.DurableAck = taskCompletionSource;
        this.PersistentState = persistentState;
        this.FollowUpEvents = followUpEvents ?? Array.Empty<IRaftEvent>();
    }
}