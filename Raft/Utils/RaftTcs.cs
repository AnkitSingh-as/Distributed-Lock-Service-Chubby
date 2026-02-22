namespace Raft;

public static class RaftTcs
{
    public static TaskCompletionSource<T> Create<T>()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    public static TaskCompletionSource Create()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);
}