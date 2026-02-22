using System.Text.Json.Serialization;
using Chubby.Core.Model;

namespace Chubby.Core.Rpc;
// Base class for all commands that modify the state machine.
// Using polymorphic deserialization with a type discriminator. The serializer will add
// a CommandType property to the JSON, and the deserializer will use it to
// instantiate the correct derived class.

[JsonPolymorphic(TypeDiscriminatorPropertyName = "CommandType")]
[JsonDerivedType(typeof(CreateSessionCommand), typeDiscriminator: "CreateSession")]
[JsonDerivedType(typeof(CloseSessionCommand), typeDiscriminator: "CloseSession")]
[JsonDerivedType(typeof(OpenCommand), typeDiscriminator: "Open")]
[JsonDerivedType(typeof(AcquireLockCommand), typeDiscriminator: "AcquireLock")]
[JsonDerivedType(typeof(SetContentsCommand), typeDiscriminator: "SetContents")]
[JsonDerivedType(typeof(SetAclCommand), typeDiscriminator: "SetAcl")]
[JsonDerivedType(typeof(ReleaseLockCommand), typeDiscriminator: "ReleaseLock")]
[JsonDerivedType(typeof(DeleteNodeCommand), typeDiscriminator: "DeleteNode")]
public abstract class BaseCommand
{
}

public class CreateSessionCommand : BaseCommand
{
    public required string SessionId { get; set; }
    public required int LeaseTimeout { get; set; }
    public required Client Client { get; set; }
    public required int Threshold { get; init; }
}


public class CloseSessionCommand : BaseCommand
{
    public required string SessionId { get; set; }
}

public class OpenCommand : BaseCommand
{
    public required string Path { get; set; }
    public required string SessionId { get; set; }
    public CreateRequestPayload? Create { get; set; }
}

public class CreateRequestPayload
{
    public required byte[] Content { get; set; }
    public bool IsEphemeral { get; set; }
    public required string[] WriteAcl { get; set; }
    public required string[] ReadAcl { get; set; }
    public required string[] ChangeAcl { get; set; }
}



// if session goes lock goes, if session goes ephemeral nodes go...

// handles have the power to close themsevles, and closing a handle means a lock has to go..


// ephemeral nodes can be tracked by session


public class LockCommand : BaseCommand
{
    public required Lock Lock { get; init; }
}

public class AcquireLockCommand : LockCommand
{
}

public class ReleaseLockCommand : LockCommand
{
}


public class DeleteNodeCommand : BaseCommand
{
    public required string Path { get; init; }
}
public class SetContentsCommand : BaseCommand
{
    public required byte[] Content { get; init; }
    public required string Path { get; init; }
    public long? ContentGenerationNumber { get; init; }
    public required string SessionId { get; init; }
}

public class SetAclCommand : BaseCommand
{
    public required string Path { get; init; }
    public required string[] WriteAcl { get; init; }
    public required string[] ReadAcl { get; init; }
    public required string[] ChangeAcl { get; init; }
    public required string SessionId { get; init; }
}

