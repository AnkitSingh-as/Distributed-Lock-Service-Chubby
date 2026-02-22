namespace Chubby.Core.Events;

public enum ClientEventType
{
    LockAcquired = 1,
    MasterFailOver = 2,
    InvalidHandleAndLock = 3,
    ConflictingLockRequest = 4,
    FileContentsModified = 5
}

[Flags]
public enum HandleEventInterest : uint
{
    None = 0,
    LockAcquired = 1 << 0,
    InvalidHandleAndLock = 1 << 1,
    ConflictingLockRequest = 1 << 2,
    FileContentsModified = 1 << 3
}

public interface Event
{
    int EventType { get; }
}
