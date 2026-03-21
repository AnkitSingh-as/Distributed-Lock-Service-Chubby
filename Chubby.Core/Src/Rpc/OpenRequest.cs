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

public class CreateRequestPayload
{
    public required byte[] Content { get; set; }
    public bool IsEphemeral { get; set; }
    public required string[] WriteAcl { get; set; }
    public required string[] ReadAcl { get; set; }
    public required string[] ChangeAcl { get; set; }
}

