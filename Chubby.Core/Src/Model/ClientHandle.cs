using System.Diagnostics.CodeAnalysis;
using Chubby.Core.Events;
using Chubby.Core.Model;
using Chubby.Core.Rpc;

namespace Chubby.Core.Model;
// we can do this, we can ask for events from them, not send it, and this would mean a way to get events 
// from clients after a failover, this would mean two types of client handle , one what we return and 
// another what they will send on handle requests....
// maybe, I will change my thinking and manage my way through this only..
public sealed class ClientHandle
{
    public required string HandleId { get; init; }
    public required string Path { get; init; }
    public required long InstanceNumber { get; init; }
    public required Permission Permission { get; init; }
    public HandleEventInterest SubscribedEventsMask { get; init; }
    public string? CheckDigit { get; set; }
    public required string SessionId { get; init; }

    public ClientHandle()
    {

    }

    [SetsRequiredMembers]
    public ClientHandle(Handle handle)
    {
        HandleId = handle.id;
        InstanceNumber = handle.InstanceNumber;
        Permission = handle.Permission;
        Path = handle.Path;
        SubscribedEventsMask = handle.SubscribedEvents;
        SessionId = handle.SessionId;
    }

}
