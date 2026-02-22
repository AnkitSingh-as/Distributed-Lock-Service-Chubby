namespace Raft;

public class Log
{
    public int Term { get; set; }
    public byte[] Command { get; set; } = Array.Empty<byte>();
    public int Index { get; set; }
}