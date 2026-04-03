public sealed class SessionRequestGate
{
    private readonly ClientSessionState _sessionState;

    public SessionRequestGate(ClientSessionState sessionState)
    {
        _sessionState = sessionState;
    }

    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken = default)
    {
        await _sessionState.WaitUntilUsableAsync(cancellationToken);
        return await action(cancellationToken);
    }

    public async Task ExecuteAsync(
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken = default)
    {
        await _sessionState.WaitUntilUsableAsync(cancellationToken);
        await action(cancellationToken);
    }
}
