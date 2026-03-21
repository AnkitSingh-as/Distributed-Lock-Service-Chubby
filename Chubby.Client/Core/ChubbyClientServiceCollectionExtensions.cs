using Chubby.Protos;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

public static class ChubbyClientServiceCollectionExtensions
{
    public static IServiceCollection AddChubbyClient(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = "Chubby")
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(sectionName);
        if (!section.Exists())
        {
            throw new InvalidOperationException($"Could not find '{sectionName}' section in configuration.");
        }

        var seedNodeAddresses = ReadSeedNodeAddresses(section);
        return services.AddChubbyClient(seedNodeAddresses);
    }

    public static IServiceCollection AddChubbyClient(
        this IServiceCollection services,
        IEnumerable<string> seedNodeAddresses)
    {
        var normalizedAddresses = NormalizeAndValidate(seedNodeAddresses);

        services.TryAddSingleton(new LeaderEndpointState(normalizedAddresses));
        services.TryAddSingleton<LeaderChannelPool>();
        services.TryAddSingleton<LeaderDiscoveryInterceptor>();

        services.TryAddSingleton(sp =>
        {
            var state = sp.GetRequiredService<LeaderEndpointState>();
            var pool = sp.GetRequiredService<LeaderChannelPool>();
            var interceptor = sp.GetRequiredService<LeaderDiscoveryInterceptor>();
            var invoker = pool.GetCallInvoker(state.CurrentLeaderAddress).Intercept(interceptor);
            return new Server.ServerClient(invoker);
        });

        services.TryAddSingleton<IChubby, ChubbyGrpcClientAdapter>();
        return services;
    }

    private static IReadOnlyList<string> ReadSeedNodeAddresses(IConfigurationSection section)
    {
        var keyedAddresses = section.Get<Dictionary<int, string>>();
        if (keyedAddresses?.Count > 0)
        {
            return keyedAddresses
                .OrderBy(kvp => kvp.Key)
                .Select(kvp => kvp.Value)
                .ToList();
        }
        throw new InvalidOperationException("Peer Address required for Chubby Client");
    }

    private static IReadOnlyList<string> NormalizeAndValidate(IEnumerable<string> addresses)
    {
        var normalized = addresses
            .Where(address => !string.IsNullOrWhiteSpace(address))
            .Select(NormalizeAddress)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalized.Count == 0)
        {
            throw new InvalidOperationException("At least one Chubby node address is required.");
        }

        return normalized;
    }

    private static string NormalizeAddress(string address)
    {
        var trimmed = address.Trim().TrimEnd('/');
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            throw new InvalidOperationException($"Invalid Chubby node address '{address}'.");

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException($"Unsupported scheme for Chubby node address '{address}'. Use http or https.");

        return trimmed;
    }
}
