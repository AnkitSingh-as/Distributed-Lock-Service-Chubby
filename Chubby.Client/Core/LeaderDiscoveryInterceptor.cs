using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.Logging;

internal sealed class LeaderDiscoveryInterceptor : Interceptor
{
    private const int MaxLeaderRedirectRetries = 2;
    private const string LeaderAddressMetadataKey = "leader-address";
    private const string RetryLeaderDiscoveryMetadataKey = "retry-leader-discovery";
    private readonly LeaderEndpointState _leaderState;
    private readonly LeaderChannelPool _channelPool;
    private readonly EpochHeaderInterceptor _epochHeaderInterceptor;
    private readonly EpochRetryInterceptor _epochRetryInterceptor;
    private readonly ILogger<LeaderDiscoveryInterceptor> _logger;

    public LeaderDiscoveryInterceptor(
        LeaderEndpointState leaderState,
        LeaderChannelPool channelPool,
        EpochHeaderInterceptor epochHeaderInterceptor,
        EpochRetryInterceptor epochRetryInterceptor,
        ILogger<LeaderDiscoveryInterceptor> logger)
    {
        _leaderState = leaderState;
        _channelPool = channelPool;
        _epochHeaderInterceptor = epochHeaderInterceptor;
        _epochRetryInterceptor = epochRetryInterceptor;
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

    // Add retry logic leader discovery logic here.
    public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncDuplexStreamingCallContinuation<TRequest, TResponse> continuation)
    {
        var maxAttempts = _leaderState.SeedNodeCount + MaxLeaderRedirectRetries;
        return new RetryingDuplexCall<TRequest, TResponse>(
            () => CreateDuplexCall(context),
            (exception, failedCall) =>
            {
                var previousLeaderAddress = _leaderState.CurrentLeaderAddress;
                return TryProcessLeaderDiscoveryFailure(exception, context, previousLeaderAddress);
            },
            maxAttempts,
            () => new Status(StatusCode.Unavailable, "Failed to connect to a leader after multiple attempts."))
            .Create();
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
            var callInvoker = CreateRetryInvoker(leaderAddress);
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

                // Case 2: Server is unavailable or explicitly says leader discovery should continue.
                if (attempt < maxAttempts && IsRetryableLeaderDiscoveryFailure(ex))
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

    private AsyncDuplexStreamingCall<TRequest, TResponse> CreateDuplexCall<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context)
        where TRequest : class
        where TResponse : class
    {
        var leaderAddress = _leaderState.CurrentLeaderAddress;
        var callInvoker = CreateRetryInvoker(leaderAddress);
        var host = GetHost(leaderAddress);
        return callInvoker.AsyncDuplexStreamingCall(context.Method, host, context.Options);
    }

    private CallInvoker CreateRetryInvoker(string leaderAddress)
    {
        return _channelPool
            .GetCallInvoker(leaderAddress)
            .Intercept(_epochHeaderInterceptor)
            .Intercept(_epochRetryInterceptor);
    }

    private bool TryProcessLeaderDiscoveryFailure<TRequest, TResponse>(
        RpcException exception,
        ClientInterceptorContext<TRequest, TResponse> context,
        string previousLeaderAddress)
        where TRequest : class
        where TResponse : class
    {
        if (TryExtractLeaderAddress(exception, out var discoveredLeader))
        {
            _logger.LogInformation(
                "Redirecting gRPC call {Method} from {OldLeader} to {NewLeader} after leader mismatch.",
                context.Method.FullName,
                previousLeaderAddress,
                discoveredLeader);
            _leaderState.UpdateLeader(discoveredLeader);
            return true;
        }

        if (IsRetryableLeaderDiscoveryFailure(exception))
        {
            _logger.LogWarning(
                "gRPC call {Method} to {Address} failed with status {StatusCode}. Trying next seed node.",
                context.Method.FullName,
                previousLeaderAddress,
                exception.StatusCode);
            _leaderState.InvalidateAndGetNextSeedNode();
            return true;
        }

        return false;
    }

    private static bool IsRetryableLeaderDiscoveryFailure(RpcException exception)
    {
        if (exception.StatusCode == StatusCode.Unavailable)
        {
            return true;
        }

        return TryGetLeaderDiscoveryDirective(exception, out _, out var shouldRetry)
               && shouldRetry;
    }

    private static bool TryExtractLeaderAddress(RpcException exception, out string leaderAddress)
    {
        return TryGetLeaderDiscoveryDirective(exception, out leaderAddress, out _)
               && !string.IsNullOrEmpty(leaderAddress);
    }

    private static bool TryGetLeaderDiscoveryDirective(
        RpcException exception,
        out string leaderAddress,
        out bool shouldRetry)
    {
        leaderAddress = string.Empty;
        shouldRetry = false;

        if (exception.StatusCode != StatusCode.FailedPrecondition)
        {
            return false;
        }

        var retryEntry = exception.Trailers.FirstOrDefault(t => t.Key == RetryLeaderDiscoveryMetadataKey);
        if (retryEntry is null || !bool.TryParse(retryEntry.Value, out var trailerRetry))
        {
            return false;
        }

        shouldRetry = trailerRetry;

        var leaderEntry = exception.Trailers.FirstOrDefault(t => t.Key == LeaderAddressMetadataKey);
        if (leaderEntry is not null && !string.IsNullOrWhiteSpace(leaderEntry.Value))
        {
            var candidateLeader = leaderEntry.Value.TrimEnd('/');
            if (Uri.TryCreate(candidateLeader, UriKind.Absolute, out var leaderUri)
                && (leaderUri.Scheme == Uri.UriSchemeHttp || leaderUri.Scheme == Uri.UriSchemeHttps))
            {
                leaderAddress = candidateLeader;
            }
        }

        return shouldRetry;
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
