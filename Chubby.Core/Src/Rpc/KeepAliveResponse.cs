using Chubby.Core.Events;

namespace Chubby.Core.Rpc;

public abstract class KeepAliveResponse
{

}

public class LeaseAboutToExpire : KeepAliveResponse
{
    public required int LeaseTimeout { get; init; }
}

public class EventAvailable : KeepAliveResponse
{
    public required List<Event> Events { get; init; }
}

// This means invalidate your cache for this session.
public class CacheInvalidation : KeepAliveResponse
{
}
