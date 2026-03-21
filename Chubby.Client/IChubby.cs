using System.Threading;
using Chubby.Protos;
using Grpc.Core;

public interface IChubby
{
    Task<CreateSessionResponse> CreateSessionAsync(CreateSessionRequest request, Metadata? headers = null, CancellationToken cancellationToken = default);
    AsyncDuplexStreamingCall<KeepAliveRequest, KeepAliveResponse> KeepAlive(Metadata? headers = null, CancellationToken cancellationToken = default);
    Task<OpenResponse> OpenAsync(OpenRequest request, Metadata? headers = null, CancellationToken cancellationToken = default);
    Task<CloseResponse> CloseAsync(CloseRequest request, Metadata? headers = null, CancellationToken cancellationToken = default);
    Task<AcquireResponse> AcquireAsync(AcquireRequest request, Metadata? headers = null, CancellationToken cancellationToken = default);
    Task<ReleaseResponse> ReleaseAsync(ReleaseRequest request, Metadata? headers = null, CancellationToken cancellationToken = default);
    Task<GetContentsAndStatResponse> GetContentsAndStatAsync(GetContentsAndStatRequest request, Metadata? headers = null, CancellationToken cancellationToken = default);
    Task<GetStatResponse> GetStatAsync(GetStatRequest request, Metadata? headers = null, CancellationToken cancellationToken = default);
    Task<ReadDirResponse> ReadDirAsync(ReadDirRequest request, Metadata? headers = null, CancellationToken cancellationToken = default);
    Task<SetContentsResponse> SetContentsAsync(SetContentsRequest request, Metadata? headers = null, CancellationToken cancellationToken = default);
    Task<SetAclResponse> SetAclAsync(SetAclRequest request, Metadata? headers = null, CancellationToken cancellationToken = default);
    Task<DeleteResponse> DeleteAsync(DeleteRequest request, Metadata? headers = null, CancellationToken cancellationToken = default);
    Task<GetSequencerResponse> GetSequencerAsync(GetSequencerRequest request, Metadata? headers = null, CancellationToken cancellationToken = default);
    Task<CheckSequencerResponse> CheckSequencerAsync(CheckSequencerRequest request, Metadata? headers = null, CancellationToken cancellationToken = default);
}
