using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.Logging;

internal sealed class EpochHeaderInterceptor : Interceptor
{
    private const string EpochHeaderKey = "epoch";
    private const string CreateSessionMethodSuffix = "/CreateSession";
    private readonly ClientSessionState _sessionState;
    private readonly ILogger<EpochHeaderInterceptor> _logger;

    public EpochHeaderInterceptor(ClientSessionState sessionState, ILogger<EpochHeaderInterceptor> logger)
    {
        _sessionState = sessionState;
        _logger = logger;
    }

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        return continuation(request, WithEpochHeader(context));
    }

    public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncDuplexStreamingCallContinuation<TRequest, TResponse> continuation)
    {
        return continuation(WithEpochHeader(context));
    }

    private ClientInterceptorContext<TRequest, TResponse> WithEpochHeader<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context)
        where TRequest : class
        where TResponse : class
    {
        if (context.Method.FullName.EndsWith(CreateSessionMethodSuffix, StringComparison.Ordinal))
        {
            return context;
        }

        var currentEpoch = _sessionState.Epoch;
        if (currentEpoch <= 0)
        {
            return context;
        }

        var headers = CloneHeaders(context.Options.Headers);
        if (!headers.Any(entry => entry.Key == EpochHeaderKey))
        {
            headers.Add(EpochHeaderKey, currentEpoch.ToString());
            _logger.LogDebug("Added epoch header {Epoch} to gRPC method {Method}.", currentEpoch, context.Method.FullName);
        }

        var options = context.Options.WithHeaders(headers);
        return new ClientInterceptorContext<TRequest, TResponse>(context.Method, context.Host, options);
    }

    private static Metadata CloneHeaders(Metadata? headers)
    {
        var clone = new Metadata();
        if (headers is null)
        {
            return clone;
        }

        foreach (var entry in headers)
        {
            if (entry.IsBinary)
            {
                clone.Add(entry.Key, entry.ValueBytes);
            }
            else
            {
                clone.Add(entry.Key, entry.Value);
            }
        }

        return clone;
    }
}
