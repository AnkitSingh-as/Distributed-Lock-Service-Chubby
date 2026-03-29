using Chubby.Protos;
using Grpc.Core;

public class ChubbyClient : IChubby
{
    private readonly ChubbyGrpcClientAdapter _adapter;
    private readonly ClientSessionState _sessionState;
    private readonly ClientCacheState _cacheState = new();
    public ChubbyClient(ChubbyGrpcClientAdapter adapter, ClientSessionState sessionState)
    {
        _adapter = adapter;
        _sessionState = sessionState;
    }

    public SessionState SessionState
    {
        get => _sessionState.SessionState;
        set => _sessionState.SessionState = value;
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
        if (SessionId is not null)
        {
            return new CreateSessionResponse { SessionId = SessionId, EpochNumber = Epoch };
        }
        var response = await _adapter.CreateSessionAsync(request, headers, cancellationToken);
        SessionId = response.SessionId;
        Epoch = response.EpochNumber;
        var KeepAliveMonitor = new KeepAliveMonitor(this);
        _ = KeepAliveMonitor.Initialize();
        return response;
    }

    public AsyncDuplexStreamingCall<KeepAliveRequest, KeepAliveResponse> KeepAlive(Metadata? headers = null, CancellationToken cancellationToken = default)
    {
        return _adapter.KeepAlive(headers, cancellationToken);
    }

    public async Task<OpenResponse> OpenAsync(OpenRequest request, Metadata? headers = null, CancellationToken cancellationToken = default)
    {
        var response = await _adapter.OpenAsync(request, headers, cancellationToken);
        return response;
    }

    public async Task<CloseResponse> CloseAsync(CloseRequest request, Metadata? headers = null, CancellationToken cancellationToken = default)
    {
        var response = await _adapter.CloseAsync(request, headers, cancellationToken);
        return response;
    }

    public Task<AcquireResponse> AcquireAsync(AcquireRequest request, Metadata? headers = null, CancellationToken cancellationToken = default)
    {
        return _adapter.AcquireAsync(request, headers, cancellationToken);
    }

    public Task<ReleaseResponse> ReleaseAsync(ReleaseRequest request, Metadata? headers = null, CancellationToken cancellationToken = default)
    {
        return _adapter.ReleaseAsync(request, headers, cancellationToken);
    }

    public Task<GetContentsAndStatResponse> GetContentsAndStatAsync(GetContentsAndStatRequest request, Metadata? headers = null, CancellationToken cancellationToken = default)
    {
        return _adapter.GetContentsAndStatAsync(request, headers, cancellationToken);
    }

    public Task<GetStatResponse> GetStatAsync(GetStatRequest request, Metadata? headers = null, CancellationToken cancellationToken = default)
    {
        return _adapter.GetStatAsync(request, headers, cancellationToken);
    }

    public Task<ReadDirResponse> ReadDirAsync(ReadDirRequest request, Metadata? headers = null, CancellationToken cancellationToken = default)
    {
        return _adapter.ReadDirAsync(request, headers, cancellationToken);
    }

    public Task<SetContentsResponse> SetContentsAsync(SetContentsRequest request, Metadata? headers = null, CancellationToken cancellationToken = default)
    {
        return _adapter.SetContentsAsync(request, headers, cancellationToken);
    }

    public Task<SetAclResponse> SetAclAsync(SetAclRequest request, Metadata? headers = null, CancellationToken cancellationToken = default)
    {
        return _adapter.SetAclAsync(request, headers, cancellationToken);
    }

    public Task<DeleteResponse> DeleteAsync(DeleteRequest request, Metadata? headers = null, CancellationToken cancellationToken = default)
    {
        return _adapter.DeleteAsync(request, headers, cancellationToken);
    }

    public Task<GetSequencerResponse> GetSequencerAsync(GetSequencerRequest request, Metadata? headers = null, CancellationToken cancellationToken = default)
    {
        return _adapter.GetSequencerAsync(request, headers, cancellationToken);
    }

    public Task<CheckSequencerResponse> CheckSequencerAsync(CheckSequencerRequest request, Metadata? headers = null, CancellationToken cancellationToken = default)
    {
        return _adapter.CheckSequencerAsync(request, headers, cancellationToken);
    }

    public void ProcessEvents(List<Event> events)
    {
        lock (_cacheProcessorLock)
        {
            events.ForEach(ev => ProtoEventMapper.Map(ev).Apply(_cacheState));
        }
    }
}
