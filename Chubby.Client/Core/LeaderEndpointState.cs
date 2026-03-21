using System.Threading;
using System.Collections.Generic;
using System;

public sealed class LeaderEndpointState
{
    private readonly IReadOnlyList<string> _seedNodeAddresses;
    private string _currentLeaderAddress;
    private int _roundRobinIndex = -1;

    public LeaderEndpointState(IReadOnlyList<string> seedNodeAddresses)
    {
        if (seedNodeAddresses is null || seedNodeAddresses.Count == 0)
        {
            throw new ArgumentException("Seed node addresses cannot be null or empty.", nameof(seedNodeAddresses));
        }
        _seedNodeAddresses = seedNodeAddresses;
        _currentLeaderAddress = _seedNodeAddresses[0];
    }

    public string CurrentLeaderAddress => Volatile.Read(ref _currentLeaderAddress);

    public int SeedNodeCount => _seedNodeAddresses.Count;

    public void UpdateLeader(string leaderAddress)
    {
        var normalized = Normalize(leaderAddress);
        Interlocked.Exchange(ref _currentLeaderAddress, normalized);
        // We found a leader, so reset the round-robin index for the next time we need to search.
        Interlocked.Exchange(ref _roundRobinIndex, -1);
    }

    /// <summary>
    /// Called when the current leader is unavailable.
    /// It updates the CurrentLeaderAddress to the next seed node in the list for the next retry attempt.
    /// </summary>
    public void InvalidateAndGetNextSeedNode()
    {
        // Use Interlocked.Increment to safely get the next index in a round-robin fashion.
        var nextIndex = Interlocked.Increment(ref _roundRobinIndex);
        var newAddress = _seedNodeAddresses[nextIndex % _seedNodeAddresses.Count];
        Interlocked.Exchange(ref _currentLeaderAddress, newAddress);
    }

    private static string Normalize(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            throw new ArgumentException("Leader address cannot be empty.", nameof(address));
        }

        return address.Trim().TrimEnd('/');
    }
}
