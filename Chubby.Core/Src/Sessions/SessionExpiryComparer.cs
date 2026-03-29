namespace Chubby.Core.Sessions;

public sealed class SessionExpiryComparer : IComparer<SessionExpiryEntry?>
{
    public int Compare(SessionExpiryEntry? x, SessionExpiryEntry? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        var expiryComparison = x.ExpiryTicks.CompareTo(y.ExpiryTicks);
        if (expiryComparison != 0) return expiryComparison;

        return string.Compare(x.SessionId, y.SessionId, StringComparison.Ordinal);
    }
}

public sealed record SessionExpiryEntry(string SessionId, long ExpiryTicks);
