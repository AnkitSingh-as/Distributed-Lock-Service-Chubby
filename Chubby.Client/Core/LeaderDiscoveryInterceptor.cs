using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.Logging;

internal sealed class LeaderDiscoveryInterceptor : Interceptor
{
    private const int MaxLeaderRedirectRetries = 2;
    private readonly LeaderEndpointState _leaderState;
    private readonly LeaderChannelPool _channelPool;
    private readonly ILogger<LeaderDiscoveryInterceptor> _logger;

    public LeaderDiscoveryInterceptor(
        LeaderEndpointState leaderState,
        LeaderChannelPool channelPool,
        ILogger<LeaderDiscoveryInterceptor> logger)
    {
        _leaderState = leaderState;
        _channelPool = channelPool;
        _logger = logger;
    }

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        var status = new Status(StatusCode.Unknown, "Call has not completed.");
        var trailers = new Metadata();
        var responseHeadersTcs = new TaskCompletionSource<Metadata>(TaskCreationOptions.RunContinuationsAsynchronously);

        var responseTask = ExecuteWithLeaderRetryAsync(
            request,
            context,
            responseHeadersTcs,
            onStatus: s => status = s,
            onTrailers: t => trailers = t);

        return new AsyncUnaryCall<TResponse>(
            responseTask,
            responseHeadersTcs.Task,
            () => status,
            () => trailers,
            static () => { });
    }

    public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncDuplexStreamingCallContinuation<TRequest, TResponse> continuation)
    {
        var leaderAddress = _leaderState.CurrentLeaderAddress;
        var callInvoker = _channelPool.GetCallInvoker(leaderAddress);
        var host = GetHost(leaderAddress);

        return callInvoker.AsyncDuplexStreamingCall(context.Method, host, context.Options);
    }

    private async Task<TResponse> ExecuteWithLeaderRetryAsync<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        TaskCompletionSource<Metadata> responseHeadersTcs,
        Action<Status> onStatus,
        Action<Metadata> onTrailers)
        where TRequest : class
        where TResponse : class
    {
        RpcException? lastException = null;
        // Allow retrying across all known nodes plus a few for redirects.
        var maxAttempts = _leaderState.SeedNodeCount + MaxLeaderRedirectRetries;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var leaderAddress = _leaderState.CurrentLeaderAddress;
            var callInvoker = _channelPool.GetCallInvoker(leaderAddress);
            var host = GetHost(leaderAddress);
            var options = context.Options;
            

            using var call = callInvoker.AsyncUnaryCall(context.Method, host, options, request);

            try
            {
                var responseHeaders = await call.ResponseHeadersAsync.ConfigureAwait(false);
                var response = await call.ResponseAsync.ConfigureAwait(false);

                responseHeadersTcs.TrySetResult(responseHeaders);
                onStatus(call.GetStatus());
                onTrailers(call.GetTrailers());

                return response;
            }
            catch (RpcException ex)
            {
                lastException = ex;
                onStatus(ex.Status);
                onTrailers(ex.Trailers);

                // Case 1: Server tells us who the new leader is.
                if (TryExtractLeaderAddress(ex, out var discoveredLeader))
                {
                    _logger.LogInformation(
                        "Redirecting gRPC call {Method} from {OldLeader} to {NewLeader} after leader mismatch.",
                        context.Method.FullName,
                        leaderAddress,
                        discoveredLeader);
                    _leaderState.UpdateLeader(discoveredLeader);
                    continue;
                }

                // Case 2: Server is unavailable or not the leader (but doesn't know who is).
                if (ex.StatusCode is StatusCode.Unavailable or StatusCode.FailedPrecondition && attempt < maxAttempts)
                {
                    _logger.LogWarning(
                        "gRPC call {Method} to {Address} failed with status {StatusCode}. Trying next seed node.",
                        context.Method.FullName,
                        leaderAddress,
                        ex.StatusCode);
                    _leaderState.InvalidateAndGetNextSeedNode();
                    continue;
                }

                responseHeadersTcs.TrySetResult(new Metadata());
                throw;
            }
        }

        responseHeadersTcs.TrySetResult(new Metadata());
        throw new RpcException(new Status(StatusCode.Unavailable, "Failed to connect to a leader after multiple attempts."));
    }

    private static bool TryExtractLeaderAddress(RpcException exception, out string leaderAddress)
    {
        leaderAddress = string.Empty;

        if (exception.StatusCode != StatusCode.FailedPrecondition)
            return false;

        var detail = exception.Status.Detail?.Trim();
        if (string.IsNullOrWhiteSpace(detail) || detail.Equals("No Leader", StringComparison.OrdinalIgnoreCase))
            return false;

        if (!Uri.TryCreate(detail, UriKind.Absolute, out var uri))
            return false;

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return false;

        leaderAddress = detail.TrimEnd('/');
        return true;
    }

    private static string GetHost(string address)
    {
        if (Uri.TryCreate(address, UriKind.Absolute, out var uri))
        {
            return uri.Authority;
        }

        return address;
    }
}
