using System.Collections.Generic;
using System.Text.Json.Serialization;
using Chubby.Core.Events;

namespace Chubby.Core.Rpc;

[JsonDerivedType(typeof(EventAvailable), typeDiscriminator: "EventAvailable")]
[JsonDerivedType(typeof(LeaseAboutToExpire), typeDiscriminator: "LeaseAboutToExpire")]
[JsonDerivedType(typeof(CacheInvalidation), typeDiscriminator: "CacheInvalidation")]
[JsonPolymorphic(TypeDiscriminatorPropertyName = "Reason")]
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

public class CacheInvalidation : KeepAliveResponse
{

}
