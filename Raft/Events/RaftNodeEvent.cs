namespace Raft;

public abstract class RaftNodeEvent<TResponse> : IRaftEvent where TResponse : Persistable

{
    public TaskCompletionSource<TResponse> tcs { get; private set; }

    protected RaftNodeEvent()
    {
        tcs = RaftTcs.Create<TResponse>();
    }

    public void Apply(RaftNode node)
    {
        try
        {
            var response = Handle(node);
            if (response.Ack is not null)
            {
                node.statePersister.Enqueue(new PersistentCommand(node.State.GetPersistentState(), response.Ack));
            }
            
            tcs.SetResult(response);
        }
        catch (Exception ex)
        {
            tcs.SetException(ex);
        }
    }

    protected abstract TResponse Handle(RaftNode node);
}
