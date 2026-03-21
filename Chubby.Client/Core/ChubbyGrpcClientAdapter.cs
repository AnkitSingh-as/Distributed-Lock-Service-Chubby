using System.Threading;
using Chubby.Protos;
using Grpc.Core;

internal sealed class ChubbyGrpcClientAdapter : IChubby
{
    private readonly Server.ServerClient _grpcClient;

    public ChubbyGrpcClientAdapter(Server.ServerClient grpcClient)
    {
        _grpcClient = grpcClient;
    }

    public async Task<CreateSessionResponse> CreateSessionAsync(CreateSessionRequest request, Metadata? headers = null, CancellationToken cancellationToken = default)
    {
        return await _grpcClient.CreateSessionAsync(request, headers: headers, cancellationToken: cancellationToken);
    }

    public AsyncDuplexStreamingCall<KeepAliveRequest, KeepAliveResponse> KeepAlive(Metadata? headers = null, CancellationToken cancellationToken = default)
    {
        return _grpcClient.KeepAlive(headers: headers, cancellationToken: cancellationToken);
    }

    public async Task<OpenResponse> OpenAsync(OpenRequest request, Metadata? headers = null, CancellationToken cancellationToken = default)
    {
        return await _grpcClient.OpenAsync(request, headers: headers, cancellationToken: cancellationToken);
    }

    public async Task<CloseResponse> CloseAsync(CloseRequest request, Metadata? headers = null, CancellationToken cancellationToken = default)
    {
        return await _grpcClient.CloseAsync(request, headers: headers, cancellationToken: cancellationToken);
    }

    public async Task<AcquireResponse> AcquireAsync(AcquireRequest request, Metadata? headers = null, CancellationToken cancellationToken = default)
    {
        return await _grpcClient.AcquireAsync(request, headers: headers, cancellationToken: cancellationToken);
    }

    public async Task<ReleaseResponse> ReleaseAsync(ReleaseRequest request, Metadata? headers = null, CancellationToken cancellationToken = default)
    {
        return await _grpcClient.ReleaseAsync(request, headers: headers, cancellationToken: cancellationToken);
    }

    public async Task<GetContentsAndStatResponse> GetContentsAndStatAsync(GetContentsAndStatRequest request, Metadata? headers = null, CancellationToken cancellationToken = default)
    {
        return await _grpcClient.GetContentsAndStatAsync(request, headers: headers, cancellationToken: cancellationToken);
    }

    public async Task<GetStatResponse> GetStatAsync(GetStatRequest request, Metadata? headers = null, CancellationToken cancellationToken = default)
    {
        return await _grpcClient.GetStatAsync(request, headers: headers, cancellationToken: cancellationToken);
    }

    public async Task<ReadDirResponse> ReadDirAsync(ReadDirRequest request, Metadata? headers = null, CancellationToken cancellationToken = default)
    {
        return await _grpcClient.ReadDirAsync(request, headers: headers, cancellationToken: cancellationToken);
    }

    public async Task<SetContentsResponse> SetContentsAsync(SetContentsRequest request, Metadata? headers = null, CancellationToken cancellationToken = default)
    {
        return await _grpcClient.SetContentsAsync(request, headers: headers, cancellationToken: cancellationToken);
    }

    public async Task<SetAclResponse> SetAclAsync(SetAclRequest request, Metadata? headers = null, CancellationToken cancellationToken = default)
    {
        return await _grpcClient.SetAclAsync(request, headers: headers, cancellationToken: cancellationToken);
    }

    public async Task<DeleteResponse> DeleteAsync(DeleteRequest request, Metadata? headers = null, CancellationToken cancellationToken = default)
    {
        return await _grpcClient.DeleteAsync(request, headers: headers, cancellationToken: cancellationToken);
    }

    public async Task<GetSequencerResponse> GetSequencerAsync(GetSequencerRequest request, Metadata? headers = null, CancellationToken cancellationToken = default)
    {
        return await _grpcClient.GetSequencerAsync(request, headers: headers, cancellationToken: cancellationToken);
    }

    public async Task<CheckSequencerResponse> CheckSequencerAsync(CheckSequencerRequest request, Metadata? headers = null, CancellationToken cancellationToken = default)
    {
        return await _grpcClient.CheckSequencerAsync(request, headers: headers, cancellationToken: cancellationToken);
    }
}
