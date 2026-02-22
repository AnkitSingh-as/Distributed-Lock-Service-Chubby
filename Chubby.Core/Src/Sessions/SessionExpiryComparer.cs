
namespace Chubby.Core.Sessions;

public class SessionExpiryComparer : IComparer<Session?>
{
    public int Compare(Session? x, Session? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        int tickComparison = x.LastActivityTicks.CompareTo(y.LastActivityTicks);
        if (tickComparison != 0) return tickComparison;

        // Tie-breaker to allow multiple sessions with the same timestamp.
        return string.Compare(x.SessionId, y.SessionId, StringComparison.Ordinal);
    }
}
