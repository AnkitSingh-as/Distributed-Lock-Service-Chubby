using Grpc.Core;
using Grpc.Core.Interceptors;

internal sealed class RetryingDuplexCall<TRequest, TResponse>
    where TRequest : class
    where TResponse : class
{
    private readonly object _gate = new();
    private readonly Func<AsyncDuplexStreamingCall<TRequest, TResponse>> _createCall;
    private readonly Func<RpcException, AsyncDuplexStreamingCall<TRequest, TResponse>, bool> _tryRecover;
    private readonly int _maxAttempts;
    private readonly Func<Status> _finalFailureStatus;
    private AsyncDuplexStreamingCall<TRequest, TResponse> _currentCall;

    public RetryingDuplexCall(
        Func<AsyncDuplexStreamingCall<TRequest, TResponse>> createCall,
        Func<RpcException, AsyncDuplexStreamingCall<TRequest, TResponse>, bool> tryRecover,
        int maxAttempts,
        Func<Status> finalFailureStatus)
    {
        _createCall = createCall;
        _tryRecover = tryRecover;
        _maxAttempts = maxAttempts;
        _finalFailureStatus = finalFailureStatus;
        _currentCall = createCall();
    }

    public AsyncDuplexStreamingCall<TRequest, TResponse> Create()
    {
        return new AsyncDuplexStreamingCall<TRequest, TResponse>(
            new RetryingClientStreamWriter(this),
            new DynamicResponseStream(this),
            GetResponseHeadersAsync(),
            () => GetCurrentCall().GetStatus(),
            () => GetCurrentCall().GetTrailers(),
            DisposeCurrentCall);
    }

    private AsyncDuplexStreamingCall<TRequest, TResponse> GetCurrentCall()
    {
        lock (_gate)
        {
            return _currentCall;
        }
    }

    private Task<Metadata> GetResponseHeadersAsync()
    {
        return GetCurrentCall().ResponseHeadersAsync;
    }

    private void DisposeCurrentCall()
    {
        lock (_gate)
        {
            _currentCall.Dispose();
        }
    }

    private bool TryReplaceCall(RpcException exception, AsyncDuplexStreamingCall<TRequest, TResponse> failedCall)
    {
        if (!_tryRecover(exception, failedCall))
        {
            return false;
        }

        lock (_gate)
        {
            if (!ReferenceEquals(_currentCall, failedCall))
            {
                return true;
            }

            _currentCall = _createCall();
        }

        failedCall.Dispose();
        return true;
    }

    public async Task WriteAsync(TRequest message, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= _maxAttempts; attempt++)
        {
            var call = GetCurrentCall();
            try
            {
                await call.RequestStream.WriteAsync(message, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (RpcException ex) when (attempt < _maxAttempts)
            {
                if (TryReplaceCall(ex, call))
                {
                    continue;
                }

                throw;
            }
        }

        throw new RpcException(_finalFailureStatus());
    }

    public Task CompleteAsync()
    {
        return GetCurrentCall().RequestStream.CompleteAsync();
    }

    public WriteOptions? GetWriteOptions()
    {
        return GetCurrentCall().RequestStream.WriteOptions;
    }

    public void SetWriteOptions(WriteOptions? value)
    {
        GetCurrentCall().RequestStream.WriteOptions = value;
    }

    public TResponse Current => GetCurrentCall().ResponseStream.Current;

    public Task<bool> MoveNext(CancellationToken cancellationToken)
    {
        return GetCurrentCall().ResponseStream.MoveNext(cancellationToken);
    }

    private sealed class RetryingClientStreamWriter : IClientStreamWriter<TRequest>
    {
        private readonly RetryingDuplexCall<TRequest, TResponse> _owner;

        public RetryingClientStreamWriter(RetryingDuplexCall<TRequest, TResponse> owner)
        {
            _owner = owner;
        }

        public WriteOptions? WriteOptions
        {
            get => _owner.GetWriteOptions();
            set => _owner.SetWriteOptions(value);
        }

        public Task CompleteAsync() => _owner.CompleteAsync();

        public Task WriteAsync(TRequest message) => _owner.WriteAsync(message, CancellationToken.None);

        public Task WriteAsync(TRequest message, CancellationToken cancellationToken) =>
            _owner.WriteAsync(message, cancellationToken);
    }

    private sealed class DynamicResponseStream : IAsyncStreamReader<TResponse>
    {
        private readonly RetryingDuplexCall<TRequest, TResponse> _owner;

        public DynamicResponseStream(RetryingDuplexCall<TRequest, TResponse> owner)
        {
            _owner = owner;
        }

        public TResponse Current => _owner.Current;

        public Task<bool> MoveNext(CancellationToken cancellationToken) => _owner.MoveNext(cancellationToken);
    }
}
