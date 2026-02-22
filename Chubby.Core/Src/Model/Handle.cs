using Chubby.Core.Events;
using Chubby.Core.Rpc;

namespace Chubby.Core.Model;
// what do I need to send back to client, and what do I store
// so that handle can be recreated....
public class Handle
{
    public string Path { get; private set; }
    public long InstanceNumber { get; private set; }
    public Permission Permission { get; private set; }
    public HandleEventInterest SubscribedEvents { get; private set; }
    public long? LockDelay { get; private set; }
    public readonly string id;
    public string SessionId { get; private set; }
    public Handle(string id, string name, Permission permission, HandleEventInterest subscribedEvents, long instanceNumber, string SessionId, long? lockDelay)
    {
        Path = name;
        Permission = permission;
        SubscribedEvents = subscribedEvents;
        InstanceNumber = instanceNumber;
        LockDelay = lockDelay;
        this.id = id;
        this.SessionId = SessionId;
    }


}
