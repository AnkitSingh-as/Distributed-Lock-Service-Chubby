using Grpc.Core;
using Grpc.Core.Interceptors;

internal sealed class EpochRetryInterceptor : Interceptor
{
    private const string EpochMetadataKey = "current-epoch";
    private readonly ClientSessionState _sessionState;

    public EpochRetryInterceptor(ClientSessionState sessionState)
    {
        _sessionState = sessionState;
    }

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        var responseHeadersTcs = new TaskCompletionSource<Metadata>(TaskCreationOptions.RunContinuationsAsynchronously);
        var status = new Status(StatusCode.Unknown, "Call has not completed.");
        var trailers = new Metadata();

        async Task<TResponse> ExecuteAsync()
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                using var call = continuation(request, context);
                try
                {
                    var responseHeaders = await call.ResponseHeadersAsync.ConfigureAwait(false);
                    var response = await call.ResponseAsync.ConfigureAwait(false);
                    responseHeadersTcs.TrySetResult(responseHeaders);
                    status = call.GetStatus();
                    trailers = call.GetTrailers();
                    return response;
                }
                catch (RpcException ex) when (ShouldRetryEpochMismatch(ex, out var updatedEpoch) && attempt == 0)
                {
                    _sessionState.Epoch = updatedEpoch;
                    status = ex.Status;
                    trailers = ex.Trailers;
                    continue;
                }
                catch (RpcException ex)
                {
                    responseHeadersTcs.TrySetResult(new Metadata());
                    status = ex.Status;
                    trailers = ex.Trailers;
                    throw;
                }
            }

            responseHeadersTcs.TrySetResult(new Metadata());
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "Epoch retry failed."));
        }

        return new AsyncUnaryCall<TResponse>(
            ExecuteAsync(),
            responseHeadersTcs.Task,
            () => status,
            () => trailers,
            static () => { });
    }

    public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncDuplexStreamingCallContinuation<TRequest, TResponse> continuation)
    {
        return new RetryingDuplexCall<TRequest, TResponse>(
            () => continuation(context),
            (exception, failedCall) =>
            {
                if (!ShouldRetryEpochMismatch(exception, out var updatedEpoch))
                {
                    return false;
                }

                _sessionState.Epoch = updatedEpoch;
                return true;
            },
            2,
            () => new Status(StatusCode.FailedPrecondition, "Epoch retry failed."))
            .Create();
    }

    private static bool ShouldRetryEpochMismatch(RpcException exception, out int epoch)
    {
        epoch = default;
        if (exception.StatusCode != StatusCode.FailedPrecondition)
        {
            return false;
        }

        var epochEntry = exception.Trailers.FirstOrDefault(entry => entry.Key == EpochMetadataKey);
        return epochEntry is not null && int.TryParse(epochEntry.Value, out epoch);
    }
}
