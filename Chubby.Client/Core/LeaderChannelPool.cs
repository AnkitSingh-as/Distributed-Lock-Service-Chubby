using System.Collections.Concurrent;
using Grpc.Core;
using Grpc.Net.Client;

internal sealed class LeaderChannelPool : IDisposable
{
    private readonly ConcurrentDictionary<string, GrpcChannel> _channels = new(StringComparer.OrdinalIgnoreCase);

    public CallInvoker GetCallInvoker(string address)
    {
        var normalizedAddress = Normalize(address);
        var channel = _channels.GetOrAdd(normalizedAddress, static x => GrpcChannel.ForAddress(x));
        return channel.CreateCallInvoker();
    }

    public void Dispose()
    {
        foreach (var channel in _channels.Values)
        {
            channel.Dispose();
        }

        _channels.Clear();
    }

    private static string Normalize(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            throw new ArgumentException("Address cannot be empty.", nameof(address));
        }

        return address.Trim().TrimEnd('/');
    }
}
