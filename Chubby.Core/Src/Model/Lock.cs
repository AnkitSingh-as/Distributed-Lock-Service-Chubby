namespace Chubby.Core.Model;

public class Lock
{
    public required string HandleId { get; set; }
    public required string Path { get; set; }
    public required long Instance { get; set; }
    public required LockType LockType { get; set; }
    public required string SessionId { get; set; }
}
