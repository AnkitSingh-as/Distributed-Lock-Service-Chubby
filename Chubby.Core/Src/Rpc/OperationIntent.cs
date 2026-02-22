namespace Chubby.Core.Rpc;

public enum Intent
{
    Read,
    Write,
    SharedLock,
    ExclusiveLock,
    ChangeAcl
}
