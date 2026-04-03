
using Microsoft.Extensions.Logging;

public sealed class ClientSessionState
{
    private readonly ILogger<ClientSessionState> _logger;
    private int _epoch;
    private string? _sessionId;
    private int _sessionState = (int)SessionState.Normal;

    public ClientSessionState(ILogger<ClientSessionState> logger)
    {
        _logger = logger;
    }

    public int Epoch
    {
        get => Volatile.Read(ref _epoch);
        set => Volatile.Write(ref _epoch, value);
    }

    public string? SessionId
    {
        get => Volatile.Read(ref _sessionId);
        set => Volatile.Write(ref _sessionId, value);
    }

    public SessionState SessionState
    {
        get => (SessionState)Volatile.Read(ref _sessionState);
        set => Volatile.Write(ref _sessionState, (int)value);
    }

    public async Task WaitUntilUsableAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            var state = SessionState;
            if (state == SessionState.Normal)
            {
                return;
            }

            if (state == SessionState.Error)
            {
                _logger.LogError("Client session reached Error state and can no longer be used.");
                throw new InvalidOperationException("The Chubby session is in the error state. Please retry after creating new session");
            }

            _logger.LogWarning("Client session is in {SessionState} state. Waiting before issuing request.", state);
            await Task.Delay(100, cancellationToken);
        }
    }
}

public enum SessionState
{
    Normal,
    Jeopardy,
    Error
}
