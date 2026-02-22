namespace Chubby.Core.Rpc;

[Flags]
public enum Permission
{
    None = 0,
    Read = 1 << 0,
    Write = 1 << 1,
    ChangeAcl = 1 << 2,
    ReadWrite = Read | Write,
}

