using Chubby.Protos;
using Grpc.Core;
using Microsoft.Extensions.Logging;

public class ChubbyClient : IChubby
{
    private readonly Server.ServerClient _grpcClient;
    private readonly ClientSessionState _sessionState;
    private readonly SessionRequestGate _requestGate;
    private readonly ClientCacheState _cacheState = new();
    private readonly ILogger<ChubbyClient> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private KeepAliveMonitor? _keepAliveMonitor;
    public ChubbyClient(
        Server.ServerClient grpcClient,
        ClientSessionState sessionState,
        ILogger<ChubbyClient> logger,
        ILoggerFactory loggerFactory)
    {
        _grpcClient = grpcClient;
        _sessionState = sessionState;
        _logger = logger;
        _loggerFactory = loggerFactory;
        _requestGate = new SessionRequestGate(sessionState);
    }

    public SessionState SessionState
    {
        get => _sessionState.SessionState;
        set
        {
            if (value == SessionState.Jeopardy)
            {
                _logger.LogWarning(
                    "Client session state transitioning to {SessionState}. Flushing client caches while preserving live handles and lock state.",
                    value);
                lock (_cacheProcessorLock)
                {
                    _cacheState.FlushCachesForFailover();
                }
            }

            if (value == SessionState.Error)
            {
                _logger.LogError("Client session entered Error state. Clearing all local session state and session identity.");
                lock (_cacheProcessorLock)
                {
                    _cacheState.ClearAll();
                }
                SessionId = null;
                Epoch = 0;
            }

            _sessionState.SessionState = value;
        }
    }

    public int Epoch
    {
        get => _sessionState.Epoch;
        set => _sessionState.Epoch = value;
    }

    public string? SessionId
    {
        get => _sessionState.SessionId;
        set => _sessionState.SessionId = value;
    }
    private readonly object _cacheProcessorLock = new object();

    // will add a interceptor to add headers to the call, by reading epoch from somewhere, so that I don't have to do it everywhere.
    // an interceptor to catch epoch mismatch which will retry through chubbyClient which will block if in jeopardy state.
    public async Task<CreateSessionResponse> CreateSessionAsync(CreateSessionRequest request, Metadata? headers = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("CreateSessionAsync called for client {ClientName}.", request.Client.Name);
        if (SessionState == SessionState.Jeopardy)
        {
            await _sessionState.WaitUntilUsableAsync(cancellationToken);
        }

        if (SessionId is not null)
        {
            _logger.LogInformation("Reusing existing client session {SessionId} at epoch {Epoch}.", SessionId, Epoch);
            return new CreateSessionResponse { SessionId = SessionId, EpochNumber = Epoch };
        }
        var response = await _grpcClient.CreateSessionAsync(request, headers, cancellationToken: cancellationToken);
        SessionId = response.SessionId;
        Epoch = response.EpochNumber;
        SessionState = SessionState.Normal;
        _keepAliveMonitor?.Dispose();
        _keepAliveMonitor = new KeepAliveMonitor(this, _loggerFactory.CreateLogger<KeepAliveMonitor>());
        _ = _keepAliveMonitor.Initialize();
        _logger.LogInformation("Created new client session {SessionId} at epoch {Epoch}.", response.SessionId, response.EpochNumber);
        return response;
    }

    public AsyncDuplexStreamingCall<KeepAliveRequest, KeepAliveResponse> KeepAlive(Metadata? headers = null, CancellationToken cancellationToken = default)
    {
        return _grpcClient.KeepAlive(headers, cancellationToken: cancellationToken);
    }

    public async Task<OpenResponse> OpenAsync(OpenRequest request, Metadata? headers = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "OpenAsync called for session {SessionId}, path {Path}, intent {Intent}, create={Create}.",
            request.SessionId,
            request.Path,
            request.Intent,
            request.Create is not null);
        if (request.Create is not null)
        {
            _logger.LogDebug("OpenAsync bypassing handle cache because create payload is present for path {Path}.", request.Path);
            return await _requestGate.ExecuteAsync(
                async token => await _grpcClient.OpenAsync(request, headers, cancellationToken: token),
                cancellationToken);
        }

        return await _requestGate.ExecuteAsync(async token =>
        {
            lock (_cacheProcessorLock)
            {
                if (_cacheState.TryGetCachedHandle(request, out var cachedHandle))
                {
                    _logger.LogInformation("OpenAsync cache hit for path {Path}. Reusing handle {HandleId}.", request.Path, cachedHandle.HandleId);
                    return new OpenResponse
                    {
                        Handle = cachedHandle
                    };
                }
            }

            _logger.LogDebug("OpenAsync cache miss for path {Path}. Calling server.", request.Path);
            return await OpenAndCacheAsync(request, headers, token);
        }, cancellationToken);
    }

    public async Task<CloseResponse> CloseAsync(CloseRequest request, Metadata? headers = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("CloseAsync called for handle {HandleId} on path {Path}.", request.Handle.HandleId, request.Handle.Path);
        return await _requestGate.ExecuteAsync(async token =>
        {
            var response = await _grpcClient.CloseAsync(request, headers, cancellationToken: token);
            lock (_cacheProcessorLock)
            {
                _cacheState.InvalidateHandleAndLock(request.Handle.HandleId);
            }
            return response;
        }, cancellationToken);
    }

    public async Task<AcquireResponse> AcquireAsync(AcquireRequest request, Metadata? headers = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("AcquireAsync called for handle {HandleId} on path {Path} with lock type {LockType}.", request.Handle.HandleId, request.Handle.Path, request.LockType);
        lock (_cacheProcessorLock)
        {
            if (_cacheState.HasLock(request.Handle.HandleId, request.LockType))
            {
                _logger.LogInformation(
                    "AcquireAsync is returning cached success because handle {HandleId} already holds lock type {LockType}.",
                    request.Handle.HandleId,
                    request.LockType);
                return new AcquireResponse();
            }
        }

        return await _requestGate.ExecuteAsync(async token =>
        {
            var response = await _grpcClient.AcquireAsync(request, headers, cancellationToken: token);
            lock (_cacheProcessorLock)
            {
                _cacheState.InvalidatePath(request.Handle.Path);
                _cacheState.RecordLockAcquired(
                    request.Handle.HandleId,
                    request.Handle.Path,
                    request.Handle.InstanceNumber,
                    request.LockType);
            }

            return response;
        }, cancellationToken);
    }

    public async Task<ReleaseResponse> ReleaseAsync(ReleaseRequest request, Metadata? headers = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("ReleaseAsync called for handle {HandleId} on path {Path} with lock type {LockType}.", request.Handle.HandleId, request.Handle.Path, request.LockType);
        return await _requestGate.ExecuteAsync(async token =>
        {
            var response = await _grpcClient.ReleaseAsync(request, headers, cancellationToken: token);
            lock (_cacheProcessorLock)
            {
                _cacheState.InvalidatePath(request.Handle.Path);
                _cacheState.RecordLockReleased(request.Handle.HandleId);
            }

            return response;
        }, cancellationToken);
    }

    public Task<GetContentsAndStatResponse> GetContentsAndStatAsync(GetContentsAndStatRequest request, Metadata? headers = null, CancellationToken cancellationToken = default)
    {
        return GetContentsAndStatInternalAsync(request, headers, cancellationToken);
    }

    public Task<GetStatResponse> GetStatAsync(GetStatRequest request, Metadata? headers = null, CancellationToken cancellationToken = default)
    {
        return GetStatInternalAsync(request, headers, cancellationToken);
    }

    public async Task<ReadDirResponse> ReadDirAsync(ReadDirRequest request, Metadata? headers = null, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("ReadDirAsync called for handle {HandleId} on path {Path}.", request.Handle.HandleId, request.Handle.Path);
        return await _requestGate.ExecuteAsync(
            async token => await _grpcClient.ReadDirAsync(request, headers, cancellationToken: token),
            cancellationToken);
    }

    public async Task<SetContentsResponse> SetContentsAsync(SetContentsRequest request, Metadata? headers = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("SetContentsAsync called for handle {HandleId} on path {Path}.", request.Handle.HandleId, request.Handle.Path);
        return await _requestGate.ExecuteAsync(async token =>
        {
            var response = await _grpcClient.SetContentsAsync(request, headers, cancellationToken: token);
            lock (_cacheProcessorLock)
            {
                _cacheState.InvalidatePath(request.Handle.Path);
            }

            return response;
        }, cancellationToken);
    }

    public async Task<SetAclResponse> SetAclAsync(SetAclRequest request, Metadata? headers = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("SetAclAsync called for handle {HandleId} on path {Path}.", request.Handle.HandleId, request.Handle.Path);
        return await _requestGate.ExecuteAsync(async token =>
        {
            var response = await _grpcClient.SetAclAsync(request, headers, cancellationToken: token);
            lock (_cacheProcessorLock)
            {
                _cacheState.InvalidatePath(request.Handle.Path);
            }

            return response;
        }, cancellationToken);
    }

    public async Task<DeleteResponse> DeleteAsync(DeleteRequest request, Metadata? headers = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("DeleteAsync called for handle {HandleId} on path {Path}.", request.Handle.HandleId, request.Handle.Path);
        return await _requestGate.ExecuteAsync(async token =>
        {
            var response = await _grpcClient.DeleteAsync(request, headers, cancellationToken: token);
            lock (_cacheProcessorLock)
            {
                _cacheState.InvalidatePath(request.Handle.Path);
                _cacheState.InvalidateHandleAndLock(request.Handle.HandleId);
            }

            return response;
        }, cancellationToken);
    }

    public async Task<GetSequencerResponse> GetSequencerAsync(GetSequencerRequest request, Metadata? headers = null, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("GetSequencerAsync called for handle {HandleId} on path {Path}.", request.Handle.HandleId, request.Handle.Path);
        return await _requestGate.ExecuteAsync(
            async token => await _grpcClient.GetSequencerAsync(request, headers: headers, cancellationToken: token),
            cancellationToken);
    }

    public async Task<CheckSequencerResponse> CheckSequencerAsync(CheckSequencerRequest request, Metadata? headers = null, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("CheckSequencerAsync called for handle {HandleId} on path {Path}.", request.Handle.HandleId, request.Handle.Path);
        return await _requestGate.ExecuteAsync(
            async token => await _grpcClient.CheckSequencerAsync(request, headers, cancellationToken: token),
            cancellationToken);
    }

    public void ProcessEvents(List<Event> events)
    {
        _logger.LogInformation("Processing {EventCount} keep-alive event(s) on client.", events.Count);
        lock (_cacheProcessorLock)
        {
            events.ForEach(ev => ProtoEventMapper.Map(ev).Apply(_cacheState));
        }
    }

    private async Task<OpenResponse> OpenAndCacheAsync(OpenRequest request, Metadata? headers, CancellationToken cancellationToken)
    {
        var response = await _grpcClient.OpenAsync(request, headers, cancellationToken: cancellationToken);
        lock (_cacheProcessorLock)
        {
            _cacheState.CacheHandle(request, response.Handle);
        }
        _logger.LogDebug("Cached handle {HandleId} for path {Path}.", response.Handle.HandleId, response.Handle.Path);

        return response;
    }

    private async Task<GetContentsAndStatResponse> GetContentsAndStatAndCacheAsync(
        GetContentsAndStatRequest request,
        Metadata? headers,
        CancellationToken cancellationToken)
    {
        var response = await _grpcClient.GetContentsAndStatAsync(request, headers, cancellationToken: cancellationToken);
        if (response.IsCacheable)
        {
            lock (_cacheProcessorLock)
            {
                _cacheState.CacheContentsAndStat(request.Handle, response.Content, response.Stat);
            }
        }

        return response;
    }

    private async Task<GetContentsAndStatResponse> GetContentsAndStatInternalAsync(
        GetContentsAndStatRequest request,
        Metadata? headers,
        CancellationToken cancellationToken)
    {
        return await _requestGate.ExecuteAsync(async token =>
        {
            lock (_cacheProcessorLock)
            {
                if (_cacheState.TryGetCachedContentsAndStat(request.Handle, out var cachedResponse))
                {
                    _logger.LogInformation("GetContentsAndStatAsync cache hit for handle {HandleId} on path {Path}.", request.Handle.HandleId, request.Handle.Path);
                    return cachedResponse;
                }
            }

            _logger.LogDebug("GetContentsAndStatAsync cache miss for handle {HandleId} on path {Path}.", request.Handle.HandleId, request.Handle.Path);
            return await GetContentsAndStatAndCacheAsync(request, headers, token);
        }, cancellationToken);
    }

    private async Task<GetStatResponse> GetStatAndCacheAsync(
        GetStatRequest request,
        Metadata? headers,
        CancellationToken cancellationToken)
    {
        var response = await _grpcClient.GetStatAsync(request, headers, cancellationToken: cancellationToken);
        if (response.IsCacheable)
        {
            lock (_cacheProcessorLock)
            {
                _cacheState.CacheStat(request.Handle, response.Stat);
            }
        }

        return response;
    }

    private async Task<GetStatResponse> GetStatInternalAsync(
        GetStatRequest request,
        Metadata? headers,
        CancellationToken cancellationToken)
    {
        return await _requestGate.ExecuteAsync(async token =>
        {
            lock (_cacheProcessorLock)
            {
                if (_cacheState.TryGetCachedStat(request.Handle, out var cachedResponse))
                {
                    _logger.LogInformation("GetStatAsync cache hit for handle {HandleId} on path {Path}.", request.Handle.HandleId, request.Handle.Path);
                    return cachedResponse;
                }
            }

            _logger.LogDebug("GetStatAsync cache miss for handle {HandleId} on path {Path}.", request.Handle.HandleId, request.Handle.Path);
            return await GetStatAndCacheAsync(request, headers, token);
        }, cancellationToken);
    }
}
