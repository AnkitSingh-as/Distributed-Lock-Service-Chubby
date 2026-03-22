using Grpc.Core;
using Chubby.Protos;
using Chubby.Core.Rpc;
using Proto = Chubby.Protos;
using Model = Chubby.Core.Model;
using CoreEvents = Chubby.Core.Events;


namespace Chubby.Services
{
    public class ChubbyService : Server.ServerBase
    {
        private readonly ChubbyRpcProxy _chubbyRpcProxy;

        private static Model.ClientHandle ToModelClientHandle(ClientHandle handle)
        {
            return new Model.ClientHandle
            {
                HandleId = handle.HandleId,
                Path = handle.Path,
                InstanceNumber = handle.InstanceNumber,
                Permission = (Core.Rpc.Permission)handle.Permission,
                SubscribedEventsMask = (CoreEvents.HandleEventInterest)handle.SubscribedEventsMask,
                CheckDigit = handle.CheckDigit,
                SessionId = handle.SessionId
            };
        }

        private static ClientHandle ToProtoClientHandle(Model.ClientHandle handle)
        {
            return new ClientHandle
            {
                HandleId = handle.HandleId,
                Path = handle.Path,
                InstanceNumber = handle.InstanceNumber,
                Permission = (Proto.Permission)handle.Permission,
                SubscribedEventsMask = (uint)handle.SubscribedEventsMask,
                CheckDigit = handle.CheckDigit,
                SessionId = handle.SessionId
            };
        }

        private static Model.LockType ToModelLockType(Proto.LockType lockType)
        {
            return lockType switch
            {
                Proto.LockType.Shared => Model.LockType.Shared,
                Proto.LockType.Exclusive => Model.LockType.Exclusive,
                _ => throw new ArgumentOutOfRangeException(nameof(lockType), lockType, "Unsupported lock type.")
            };
        }

        private static Event ToProtoEvent(CoreEvents.Event @event)
        {
            var eventType = @event switch
            {
                CoreEvents.LockAcquiredEvent => EventType.LockAcquired,
                CoreEvents.MasterFailOverEvent => EventType.MasterFailOver,
                CoreEvents.InvalidHandleAndLockEvent => EventType.InvalidHandleAndLock,
                CoreEvents.ConflictingLockRequestEvent => EventType.ConflictingLockRequest,
                CoreEvents.FileContentsModifiedEvent => EventType.FileContentsModified,
                _ => EventType.None
            };

            return new Event { Type = eventType };
        }

        public ChubbyService(ChubbyRpcProxy chubbyRpcProxy)
        {
            _chubbyRpcProxy = chubbyRpcProxy;
        }

        public override async Task<CreateSessionResponse> CreateSession(CreateSessionRequest request, ServerCallContext context)
        {
            var (sessionId, epochNumber) = await _chubbyRpcProxy.CreateSession(new Model.Client { Name = request.Client.Name });
            return new CreateSessionResponse
            {
                SessionId = sessionId,
                EpochNumber = epochNumber
            };
        }

        public override async Task KeepAlive(IAsyncStreamReader<KeepAliveRequest> requestStream, IServerStreamWriter<Proto.KeepAliveResponse> responseStream, ServerCallContext context)
        {
            // Read from the client's request stream, Keep in mind that the loop ends when the client closes the stream.
            await foreach (var request in requestStream.ReadAllAsync(context.CancellationToken))
            {
                var keepAliveResult = await _chubbyRpcProxy.KeepAlive(request.SessionId);
                var response = new Proto.KeepAliveResponse();
                switch (keepAliveResult)
                {
                    case Core.Rpc.LeaseAboutToExpire leaseAboutToExpire:
                        response.LeaseAboutToExpire = new Proto.LeaseAboutToExpire
                        {
                            LeaseTimeout = leaseAboutToExpire.LeaseTimeout
                        };
                        break;
                    case Core.Rpc.EventAvailable eventAvailable:
                        response.EventAvailable = new Proto.EventAvailable
                        {};
                        response.EventAvailable.Events.AddRange(eventAvailable.Events.Select(ToProtoEvent));
                        break;
                    case Core.Rpc.CacheInvalidation:
                        response.CacheInvalidation = new Proto.CacheInvalidation();
                        break;
                    default:
                        break;
                }
                await responseStream.WriteAsync(response);
            }
        }

        public override async Task<Proto.OpenResponse> Open(Proto.OpenRequest request, ServerCallContext context)
        {
            var proxyRequest = new Core.Rpc.OpenRequest
            {
                SessionId = request.SessionId,
                Path = request.Path,
                Intent = (Core.Rpc.Intent)request.Intent,
                SubscribedEventsMask = (CoreEvents.HandleEventInterest)request.SubscribedEventsMask,
                LockDelay = request.LockDelay,
                Create = request.Create != null ? new Core.Rpc.CreateRequestPayload
                {
                    Content = request.Create.Content.ToByteArray(),
                    IsEphemeral = request.Create.IsEphemeral,
                    WriteAcl = request.Create.WriteAcl.ToArray(),
                    ReadAcl = request.Create.ReadAcl.ToArray(),
                    ChangeAcl = request.Create.ChangeAcl.ToArray()
                } : null
            };
            var proxyResponse = await _chubbyRpcProxy.Open(proxyRequest);
            return new Proto.OpenResponse
            {
                Created = proxyResponse.Created,
                Handle = ToProtoClientHandle(proxyResponse.Handle)
            };
        }

        public override Task<CloseResponse> Close(CloseRequest request, ServerCallContext context)
        {
            // TODO: _chubbyRpcProxy.Close(request.Handle);
            return Task.FromResult(new CloseResponse());
        }

        public override async Task<AcquireResponse> Acquire(AcquireRequest request, ServerCallContext context)
        {
            var clientHandle = ToModelClientHandle(request.Handle);
            await _chubbyRpcProxy.AcquireLock(clientHandle, ToModelLockType(request.LockType));
            return new AcquireResponse();
        }

        public override async Task<ReleaseResponse> Release(ReleaseRequest request, ServerCallContext context)
        {
            var clientHandle = ToModelClientHandle(request.Handle);
            await _chubbyRpcProxy.ReleaseLock(clientHandle, ToModelLockType(request.LockType));
            return new ReleaseResponse();
        }

        public override async Task<GetContentsAndStatResponse> GetContentsAndStat(GetContentsAndStatRequest request, ServerCallContext context)
        {
            var clientHandle = ToModelClientHandle(request.Handle);
            var response = await _chubbyRpcProxy.GetContentsAndStat(clientHandle);
            return new GetContentsAndStatResponse
            {
                Content = Google.Protobuf.ByteString.CopyFrom(response.Content),
                Stat = new Stat
                {
                    InstanceNumber = response.Stat.InstanceNumber,
                    ContentGenerationNumber = response.Stat.ContentGenerationNumber,
                    LockGenerationNumber = response.Stat.LockGenerationNumber,
                    AclGenerationNumber = response.Stat.AclGenerationNumber,
                    ContentLength = response.Stat.ContentLength
                }
            };
        }

        public override async Task<GetStatResponse> GetStat(GetStatRequest request, ServerCallContext context)
        {
            var clientHandle = ToModelClientHandle(request.Handle);
            var response = await _chubbyRpcProxy.GetStat(clientHandle);
            return new GetStatResponse
            {
                Stat = new Stat
                {
                    InstanceNumber = response.InstanceNumber,
                    ContentGenerationNumber = response.ContentGenerationNumber,
                    LockGenerationNumber = response.LockGenerationNumber,
                    AclGenerationNumber = response.AclGenerationNumber,
                    ContentLength = response.ContentLength
                }
            };
        }

        public override async Task<ReadDirResponse> ReadDir(ReadDirRequest request, ServerCallContext context)
        {
            var clientHandle = ToModelClientHandle(request.Handle);
            var response = await _chubbyRpcProxy.ReadDir(clientHandle);
            return new ReadDirResponse { Children = { response } };
        }

        public override async Task<SetContentsResponse> SetContents(SetContentsRequest request, ServerCallContext context)
        {
            var clientHandle = ToModelClientHandle(request.Handle);
            await _chubbyRpcProxy.SetContents(clientHandle, request.Content.ToByteArray(), request.ContentGenerationNumber);
            return new SetContentsResponse();
        }

        public override async Task<SetAclResponse> SetAcl(SetAclRequest request, ServerCallContext context)
        {
            var clientHandle = ToModelClientHandle(request.Handle);
            await _chubbyRpcProxy.SetAcl(request.Handle.SessionId, clientHandle, request.WriteAcl.ToArray(), request.ReadAcl.ToArray(), request.ChangeAcl.ToArray());
            return new SetAclResponse();
        }

        public override async Task<DeleteResponse> Delete(DeleteRequest request, ServerCallContext context)
        {
            var clientHandle = ToModelClientHandle(request.Handle);
            await _chubbyRpcProxy.Delete(clientHandle);
            return new DeleteResponse();
        }

        public override async Task<GetSequencerResponse> GetSequencer(GetSequencerRequest request, ServerCallContext context)
        {
            var clientHandle = ToModelClientHandle(request.Handle);
            var sequencer = await _chubbyRpcProxy.GetSequencer(clientHandle);
            return new GetSequencerResponse { Sequencer = sequencer };
        }

        public override async Task<CheckSequencerResponse> CheckSequencer(CheckSequencerRequest request, ServerCallContext context)
        {
            var clientHandle = ToModelClientHandle(request.Handle);
            var valid = await _chubbyRpcProxy.CheckSequencer(clientHandle, request.Sequencer);
            return new CheckSequencerResponse { Valid = valid };
        }
    }
}
