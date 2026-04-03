using Proto = Chubby.Protos;

public sealed class Node
{
    public required string Path { get; init; }
    public byte[]? Content { get; init; }
    public required Proto.Stat Stat { get; init; }
}
