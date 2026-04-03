using Chubby.Core.Events;

namespace Chubby.Core.Rpc;

public abstract class KeepAliveResponse
{

}

public class LeaseAboutToExpire : KeepAliveResponse
{
    public required int LeaseTimeout { get; init; }
}

public sealed class DeliveredSessionEvent
{
    public required long SequenceNumber { get; init; }
    public required Event Event { get; init; }
}

public class EventAvailable : KeepAliveResponse
{
    public required List<DeliveredSessionEvent> Events { get; init; }
}
