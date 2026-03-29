
public sealed class ClientSessionState
{
    private int _epoch;
    private string? _sessionId;
    private int _sessionState = (int)SessionState.Normal;

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

}

public enum SessionState
{
    Normal,
    Jeopardy,
    Error
}
