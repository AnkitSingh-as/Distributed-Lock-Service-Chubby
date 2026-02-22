using Chubby.Core.Events;
using Chubby.Core.Model;

namespace Chubby.Core.Rpc;

public class OpenRequest : Request
{
    public required string Path { get; set; }
    public required Intent Intent { get; set; }
    public HandleEventInterest SubscribedEventsMask { get; set; }
    public long? LockDelay { get; set; }
    public CreateRequestPayload? Create { get; set; }
    public required string SessionId { get; set; }
}

