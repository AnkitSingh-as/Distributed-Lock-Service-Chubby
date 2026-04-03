using Grpc.Core;
using Chubby.Protos;
using Chubby.Core.Rpc;
using Microsoft.Extensions.Logging;
using Proto = Chubby.Protos;
using Model = Chubby.Core.Model;
using CoreEvents = Chubby.Core.Events;


namespace Chubby.Services
{
    public class ChubbyService : Server.ServerBase
    {
        private readonly ChubbyRpcProxy _chubbyRpcProxy;
        private readonly ILogger<ChubbyService> _logger;
        private readonly ChubbyRaftOrchestrator _orchestrator;

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
            return @event switch
            {
                CoreEvents.LockAcquiredEvent lockAcquiredEvent => new Event
                {
                    LockAcquired = new Proto.LockAcquiredEvent
                    {
                        Path = lockAcquiredEvent.Path,
                        InstanceNumber = lockAcquiredEvent.InstanceNumber,
                        LockType = (Proto.LockType)lockAcquiredEvent.LockType,
                        HandleId = lockAcquiredEvent.HandleId
                    }
                },
                CoreEvents.MasterFailOverEvent masterFailOverEvent => new Event
                {
                    MasterFailOver = new Proto.MasterFailOverEvent
                    {
                        EpochNumber = masterFailOverEvent.EpochNumber
                    }
                },
                CoreEvents.InvalidHandleAndLockEvent invalidHandleAndLockEvent => new Event
                {
                    InvalidHandleAndLock = new Proto.InvalidHandleAndLockEvent
                    {
                        HandleId = invalidHandleAndLockEvent.HandleId
                    }
                },
                CoreEvents.ConflictingLockRequestEvent conflictingLockRequestEvent => new Event
                {
                    ConflictingLockRequest = new Proto.ConflictingLockRequestEvent
                    {
                        Path = conflictingLockRequestEvent.Path,
                        InstanceNumber = conflictingLockRequestEvent.InstanceNumber,
                        LockType = (Proto.LockType)conflictingLockRequestEvent.LockType,
                    }
                },
                CoreEvents.FileContentsModifiedEvent fileContentsModifiedEvent => new Event
                {
                    FileContentsModified = new Proto.FileContentsModifiedEvent
                    {
                        Path = fileContentsModifiedEvent.Path,
                        InstanceNumber = fileContentsModifiedEvent.InstanceNumber,
                        ContentGenerationNumber = fileContentsModifiedEvent.ContentGenerationNumber
                    }
                },
                _ => throw new ArgumentOutOfRangeException(nameof(@event), @event.GetType(), "Unsupported event type.")
            };
        }

        private static Proto.DeliveredEvent ToProtoDeliveredEvent(Core.Rpc.DeliveredSessionEvent deliveredEvent)
        {
            return new Proto.DeliveredEvent
            {
                SequenceNumber = deliveredEvent.SequenceNumber,
                Event = ToProtoEvent(deliveredEvent.Event)
            };
        }

        public ChubbyService(ChubbyRpcProxy chubbyRpcProxy, ChubbyRaftOrchestrator orchestrator, ILogger<ChubbyService> logger)
        {
            _chubbyRpcProxy = chubbyRpcProxy;
            _orchestrator = orchestrator;
            _logger = logger;
        }

        public override async Task<CreateSessionResponse> CreateSession(CreateSessionRequest request, ServerCallContext context)
        {
            _logger.LogInformation("CreateSession called for client {ClientName}.", request.Client.Name);
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
                EnsureLeaderForKeepAlive();
                _logger.LogDebug(
                    "KeepAlive stream request received for session {SessionId} with ackedThroughSequence {AckedThroughSequence}.",
                    request.SessionId,
                    request.AckedThroughSequence);
                try
                {
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                        context.CancellationToken,
                        _orchestrator.GetLeaderLifetimeToken());

                    var keepAliveResult = await _chubbyRpcProxy.KeepAlive(
                        request.SessionId,
                        request.AckedThroughSequence,
                        linkedCts.Token);
                    var response = new Proto.KeepAliveResponse();
                    switch (keepAliveResult)
                    {
                        case Core.Rpc.LeaseAboutToExpire leaseAboutToExpire:
                            response.LeaseAboutToExpire = new Proto.LeaseAboutToExpire
                            {
                                LeaseTimeout = leaseAboutToExpire.LeaseTimeout
                            };
                            _logger.LogDebug(
                                "KeepAlive stream responding to session {SessionId} with LeaseAboutToExpire({LeaseTimeout}).",
                                request.SessionId,
                                leaseAboutToExpire.LeaseTimeout);
                            break;
                        case Core.Rpc.EventAvailable eventAvailable:
                            response.EventAvailable = new Proto.EventAvailable{};
                            response.EventAvailable.Events.AddRange(eventAvailable.Events.Select(ToProtoDeliveredEvent));
                            _logger.LogInformation(
                                "KeepAlive stream responding to session {SessionId} with {EventCount} event(s).",
                                request.SessionId,
                                eventAvailable.Events.Count);
                            break;
                        default:
                            throw new InvalidOperationException($"Unsupported keep-alive response type '{keepAliveResult.GetType().Name}'.");
                    }

                    await responseStream.WriteAsync(response);
                }
                catch (OperationCanceledException) when (!context.CancellationToken.IsCancellationRequested && !_chubbyRpcProxy.IsLeader())
                {
                    _logger.LogWarning(
                        "KeepAlive stream for session {SessionId} was canceled because this node is no longer leader.",
                        request.SessionId);
                    ThrowLeaderMismatch();
                }
            }
        }

        private void EnsureLeaderForKeepAlive()
        {
            if (_chubbyRpcProxy.IsLeader())
            {
                return;
            }

            ThrowLeaderMismatch();
        }

        private void ThrowLeaderMismatch()
        {
            var leaderAddress = _chubbyRpcProxy.GetLeaderAddress();
            throw new RpcException(
                new Grpc.Core.Status(Grpc.Core.StatusCode.FailedPrecondition, "Leader mismatch"),
                new Metadata
                {
                    { RequestValidationInterceptor.RetryLeaderDiscoveryMetadataKey, bool.TrueString },
                    { RequestValidationInterceptor.LeaderAddressMetadataKey, leaderAddress ?? string.Empty }
                });
        }

        public override async Task<Proto.OpenResponse> Open(Proto.OpenRequest request, ServerCallContext context)
        {
            _logger.LogInformation(
                "Open called for session {SessionId}, path {Path}, intent {Intent}, create={Create}.",
                request.SessionId,
                request.Path,
                request.Intent,
                request.Create is not null);
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
            _logger.LogInformation("Acquire called for handle {HandleId} on path {Path} with lock type {LockType}.", request.Handle.HandleId, request.Handle.Path, request.LockType);
            var clientHandle = ToModelClientHandle(request.Handle);
            await _chubbyRpcProxy.AcquireLock(clientHandle, ToModelLockType(request.LockType));
            return new AcquireResponse();
        }

        public override async Task<ReleaseResponse> Release(ReleaseRequest request, ServerCallContext context)
        {
            _logger.LogInformation("Release called for handle {HandleId} on path {Path} with lock type {LockType}.", request.Handle.HandleId, request.Handle.Path, request.LockType);
            var clientHandle = ToModelClientHandle(request.Handle);
            await _chubbyRpcProxy.ReleaseLock(clientHandle, ToModelLockType(request.LockType));
            return new ReleaseResponse();
        }

        public override async Task<GetContentsAndStatResponse> GetContentsAndStat(GetContentsAndStatRequest request, ServerCallContext context)
        {
            _logger.LogDebug("GetContentsAndStat called for handle {HandleId} on path {Path}.", request.Handle.HandleId, request.Handle.Path);
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
                },
                IsCacheable = response.IsCacheable
            };
        }

        public override async Task<GetStatResponse> GetStat(GetStatRequest request, ServerCallContext context)
        {
            _logger.LogDebug("GetStat called for handle {HandleId} on path {Path}.", request.Handle.HandleId, request.Handle.Path);
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
                },
                IsCacheable = response.IsCacheable
            };
        }

        public override async Task<ReadDirResponse> ReadDir(ReadDirRequest request, ServerCallContext context)
        {
            _logger.LogDebug("ReadDir called for handle {HandleId} on path {Path}.", request.Handle.HandleId, request.Handle.Path);
            var clientHandle = ToModelClientHandle(request.Handle);
            var response = await _chubbyRpcProxy.ReadDir(clientHandle);
            return new ReadDirResponse { Children = { response } };
        }

        public override async Task<SetContentsResponse> SetContents(SetContentsRequest request, ServerCallContext context)
        {
            _logger.LogInformation("SetContents called for handle {HandleId} on path {Path}.", request.Handle.HandleId, request.Handle.Path);
            var clientHandle = ToModelClientHandle(request.Handle);
            await _chubbyRpcProxy.SetContents(clientHandle, request.Content.ToByteArray(), request.ContentGenerationNumber);
            return new SetContentsResponse();
        }

        public override async Task<SetAclResponse> SetAcl(SetAclRequest request, ServerCallContext context)
        {
            _logger.LogInformation("SetAcl called for handle {HandleId} on path {Path}.", request.Handle.HandleId, request.Handle.Path);
            var clientHandle = ToModelClientHandle(request.Handle);
            await _chubbyRpcProxy.SetAcl(request.Handle.SessionId, clientHandle, request.WriteAcl.ToArray(), request.ReadAcl.ToArray(), request.ChangeAcl.ToArray());
            return new SetAclResponse();
        }

        public override async Task<DeleteResponse> Delete(DeleteRequest request, ServerCallContext context)
        {
            _logger.LogInformation("Delete called for handle {HandleId} on path {Path}.", request.Handle.HandleId, request.Handle.Path);
            var clientHandle = ToModelClientHandle(request.Handle);
            await _chubbyRpcProxy.Delete(clientHandle);
            return new DeleteResponse();
        }

        public override async Task<GetSequencerResponse> GetSequencer(GetSequencerRequest request, ServerCallContext context)
        {
            _logger.LogDebug("GetSequencer called for handle {HandleId} on path {Path}.", request.Handle.HandleId, request.Handle.Path);
            var clientHandle = ToModelClientHandle(request.Handle);
            var sequencer = await _chubbyRpcProxy.GetSequencer(clientHandle);
            return new GetSequencerResponse { Sequencer = sequencer };
        }

        public override async Task<CheckSequencerResponse> CheckSequencer(CheckSequencerRequest request, ServerCallContext context)
        {
            _logger.LogDebug("CheckSequencer called for handle {HandleId} on path {Path}.", request.Handle.HandleId, request.Handle.Path);
            var clientHandle = ToModelClientHandle(request.Handle);
            var valid = await _chubbyRpcProxy.CheckSequencer(clientHandle, request.Sequencer);
            return new CheckSequencerResponse { Valid = valid };
        }
    }
}
