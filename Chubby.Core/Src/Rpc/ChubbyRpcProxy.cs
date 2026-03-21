using System.Text.Json;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Raft;
using Chubby.Core.Model;
using Chubby.Core.StateMachine;
using Chubby.Core.Sessions;
using Chubby.Core.Utils;
using Chubby.Core.Events;


namespace Chubby.Core.Rpc;
// Add Verify epoch number at top grpc level, so that it is validated for each rpc...
// every request needs to have epoch number.


// for read requests, do I need to exchange heartbeats before responding, to ensure no other leader has been elected....
// need to think about reads, to verify leader is sending the response, so that reads are linearizable.
public class ChubbyRpcProxy
{
    private readonly ChubbyCore _chubby;
    private readonly INodeEnvelope _nodeEnvelope;
    private readonly ChubbyConfig _chubbyConfig;
    private readonly CheckDigitCalculator _checkDigitCalculator;
    public SessionScheduler SessionScheduler { get; private set; }
    public ChubbyRpcProxy(ChubbyCore chubby, INodeEnvelope nodeEnvelope, ChubbyConfig config)
    {
        _chubby = chubby;
        _nodeEnvelope = nodeEnvelope;
        _chubbyConfig = config;
        _checkDigitCalculator = new CheckDigitCalculator(new HmacCheckDigitStrategy());
        SessionScheduler = new SessionScheduler(chubby, this, config);
    }

    private static readonly ConcurrentDictionary<string, RequireAttribute?> _attributeCache = new();

    private void CheckPermission(Permission clientPermission, [CallerMemberName] string memberName = "")
    {
        var attribute = _attributeCache.GetOrAdd(memberName, name =>
        {
            var method = GetType().GetMethod(name);
            return method?.GetCustomAttribute<RequireAttribute>();
        });

        if (attribute == null)
        {
            return;
        }

        bool isAllowed = clientPermission.HasFlag(attribute.RequiredPermission);
        if (!isAllowed)
        {
            throw new InvalidOperationException($"Operation Not Allowed. Requires {attribute.RequiredPermission}, but handle has {clientPermission}.");
        }
    }

    public int CurrentEpochNumber()
    {
        return _nodeEnvelope.CurrentEpochNumber();
    }

    public bool IsLeader()
    {
        return _nodeEnvelope.IsLeader();
    }

    public string? GetLeaderAddress()
    {
        return _nodeEnvelope.GetLeaderAddress();
    }

    public async Task<OpenResponse> Open(OpenRequest request)
    {
        bool nodeCreated = false;
        if (request.Create is not null)
        {
            var openCommand = new OpenCommand
            {
                Path = request.Path,
                SessionId = request.SessionId,
                Create = new CreateRequestPayload
                {
                    Content = request.Create.Content,
                    IsEphemeral = request.Create.IsEphemeral,
                    WriteAcl = request.Create.WriteAcl,
                    ReadAcl = request.Create.ReadAcl,
                    ChangeAcl = request.Create.ChangeAcl
                }
            };

            var commandBytes = JsonSerializer.SerializeToUtf8Bytes(openCommand as BaseCommand);
            var result = await _nodeEnvelope.WriteAsync(commandBytes);

            if (result is bool created)
            {
                nodeCreated = created;
            }
            else
            {
                nodeCreated = false;
            }
        }
        // TODO: in else block, need to add acl checks, because remember acl checks are only done at creation.

        var response = CreateHandleForOpen(request);
        response.Created = nodeCreated;
        return response;
    }

    public async Task<(string sessionId, int epochNumber)> CreateSession(Client client)
    {
        var sessionId = Guid.NewGuid().ToString();
        var createSessionCmd = new CreateSessionCommand
        {
            SessionId = sessionId,
            LeaseTimeout = _chubbyConfig.LeaseTimeout,
            Client = client,
            Threshold = _chubbyConfig.Threshold,
        };

        var commandBytes = JsonSerializer.SerializeToUtf8Bytes(createSessionCmd as BaseCommand);
        await _nodeEnvelope.WriteAsync(commandBytes);
        var session = _chubby.GetSession(sessionId);
        // The session is new, so its activity is current. Add it to the scheduler.
        SessionScheduler.AddOrUpdate(session);
        return (sessionId, CurrentEpochNumber());
    }

    public async Task CloseSession(string sessionId)
    {
        var closeSessionCmd = new CloseSessionCommand { SessionId = sessionId };
        var commandBytes = JsonSerializer.SerializeToUtf8Bytes(closeSessionCmd as BaseCommand);
        await _nodeEnvelope.WriteAsync(commandBytes);
    }


    [Require(Permission.Read)]
    public Task<ContentAndStat> GetContentsAndStat(ClientHandle clientHandle)
    {
        CheckPermission(clientHandle.Permission);
        var (node, _) = ValidateHandleAndGetNodeAndHandle(clientHandle);

        var result = new ContentAndStat()
        {
            Content = node.content,
            Stat = node.GetStat()
        };
        return Task.FromResult(result);
    }

    [Require(Permission.Read)]
    public Task<Stat> GetStat(ClientHandle clientHandle)
    {
        CheckPermission(clientHandle.Permission);
        var (node, _) = ValidateHandleAndGetNodeAndHandle(clientHandle);
        return Task.FromResult(node.GetStat());
    }

    [Require(Permission.Read)]
    public Task<string[]> ReadDir(ClientHandle clientHandle)
    {
        CheckPermission(clientHandle.Permission);
        var (node, _) = ValidateHandleAndGetNodeAndHandle(clientHandle);
        var childNames = _chubby.GetChildNodeNames(node.name);
        return Task.FromResult(childNames);
    }

    [Require(Permission.Write)]
    public async Task SetContents(ClientHandle clientHandle, byte[] content, long? contentGenerationNumber)
    {
        CheckPermission(clientHandle.Permission);
        var (node, _) = ValidateHandleAndGetNodeAndHandle(clientHandle);

        // ContentGenerationNumber (Optimistic Concurrency / CAS) ---
        // The contentGenerationNumber is passed down into the command for the state machine.
        // The state machine will perform a Compare-And-Set (CAS) operation, ensuring
        // the write only succeeds if the node's content hasn't been modified by
        // another client since the caller last read it. This prevents lost updates.
        var setContentsCmd = new SetContentsCommand
        {
            Path = node.name,
            Content = content,
            ContentGenerationNumber = contentGenerationNumber,
            SessionId = clientHandle.SessionId
        };
        var commandBytes = JsonSerializer.SerializeToUtf8Bytes(setContentsCmd as BaseCommand);
        await _nodeEnvelope.WriteAsync(commandBytes);
        var session = _chubby.GetSession(clientHandle.SessionId);
    }

    [Require(Permission.ChangeAcl)]
    public async Task SetAcl(string sessionId, ClientHandle clientHandle, string[] writeAcl, string[] readAcl, string[] changeAcl)
    {
        CheckPermission(clientHandle.Permission);
        var (node, _) = ValidateHandleAndGetNodeAndHandle(clientHandle);
        var setAclCmd = new SetAclCommand
        {
            Path = node.name,
            WriteAcl = writeAcl,
            ReadAcl = readAcl,
            ChangeAcl = changeAcl,
            SessionId = sessionId
        };
        var commandBytes = JsonSerializer.SerializeToUtf8Bytes(setAclCmd as BaseCommand);
        await _nodeEnvelope.WriteAsync(commandBytes);
    }

    [Require(Permission.Write)]
    public async Task AcquireLock(ClientHandle clientHandle, LockType @lock)
    {
        CheckPermission(clientHandle.Permission);
        var (node, _) = ValidateHandleAndGetNodeAndHandle(clientHandle);
        var acquireLockCmd = new AcquireLockCommand
        {
            Lock = new Lock()
            {
                Path = node.name,
                Instance = node.Instance,
                LockType = @lock,
                SessionId = clientHandle.SessionId,
            }
        };
        var commandBytes = JsonSerializer.SerializeToUtf8Bytes(acquireLockCmd as BaseCommand);
        var result = await _nodeEnvelope.WriteAsync(commandBytes);
        if (result is not true)
        {
            throw new InvalidOperationException("Failed to acquire lock. A conflicting lock may exist.");
        }
    }

    [Require(Permission.Write)]
    public async Task ReleaseLock(ClientHandle clientHandle, LockType @lock)
    {
        CheckPermission(clientHandle.Permission);
        var (node, _) = ValidateHandleAndGetNodeAndHandle(clientHandle);
        var lockInstance = new Lock
        {
            Path = clientHandle.Path,
            Instance = clientHandle.InstanceNumber,
            LockType = @lock,
            SessionId = clientHandle.SessionId
        };

        var releaseLockCmd = new ReleaseLockCommand
        {
            Lock = lockInstance
        };
        var commandBytes = JsonSerializer.SerializeToUtf8Bytes(releaseLockCmd as BaseCommand);
        // Release is idempotent. We submit the command but don't need to fail if the lock
        // was already gone. The state machine will handle this
        await _nodeEnvelope.WriteAsync(commandBytes);
    }

    [Require(Permission.Write)]
    public async Task Delete(ClientHandle clientHandle)
    {
        CheckPermission(clientHandle.Permission);
        var (_, _) = ValidateHandleAndGetNodeAndHandle(clientHandle);

        var deleteNodeCmd = new DeleteNodeCommand { Path = clientHandle.Path };
        var commandBytes = JsonSerializer.SerializeToUtf8Bytes(deleteNodeCmd as BaseCommand);
        var result = await _nodeEnvelope.WriteAsync(commandBytes);
        if (result is not true)
        {
            throw new InvalidOperationException("Failed to delete node. It may have children or may not exist.");
        }
    }

    [Require(Permission.Read)]
    public async Task<string?> GetSequencer(ClientHandle clientHandle)
    {
        CheckPermission(clientHandle.Permission);
        var (node, _) = ValidateHandleAndGetNodeAndHandle(clientHandle);
        return await Task.FromResult(_chubby.GetNodeSequencer(node));
    }

    [Require(Permission.Read)]
    public async Task<bool> CheckSequencer(ClientHandle clientHandle, string sequencer)
    {
        CheckPermission(clientHandle.Permission);
        var (node, _) = ValidateHandleAndGetNodeAndHandle(clientHandle);
        return await Task.FromResult(_chubby.CheckNodeSequencer(node, sequencer));
    }

    public async Task<KeepAliveResponse> KeepAlive(string sessionId)
    {
        var session = _chubby.GetSession(sessionId);
        session.UpdateLastActivity();
        SessionScheduler.AddOrUpdate(session);
        return await session.KeepAlive();
    }

    private OpenResponse CreateHandleForOpen(OpenRequest request)
    {
        if (!_chubby.TryGetNode(request.Path, out (Node node, Status status) record))
        {
            // This should ideally not happen if the OpenCommand was successful
            throw new Exception("Node Not Found after creation/open.");
        }

        var permission = PermissionIntentMapping.GetPermission(request.Intent);
        var id = Guid.NewGuid().ToString();
        var handle = new Handle(
            id,
            request.Path,
            permission,
            request.SubscribedEventsMask,
            record.node.Instance,
            request.SessionId,
            request.LockDelay);
        var responseHandle = new ClientHandle(handle);

        var checkDigit = _checkDigitCalculator.CalculateCheckDigit(responseHandle);
        responseHandle.CheckDigit = checkDigit;
        _chubby.AddHandleToSession(handle);

        // Any activity that creates a handle should be considered session activity.
        var session = _chubby.GetSession(request.SessionId);
        SessionScheduler.Remove(session);

        return new OpenResponse()
        {
            Handle = responseHandle
        };
    }

    // can be refactored to create classes that run their own validations, and then call them here, to make it more readable, maintainable.
    // this class knows too much about the internals of the node and handle, so maybe we can refactor it to make it more cohesive, and single responsibility, but for now, I will keep it like this, to move faster.
    private (Node node, Handle handle) ValidateHandleAndGetNodeAndHandle(ClientHandle clientHandle)
    {
        var verified = _checkDigitCalculator.VerifyCheckDigit(clientHandle, clientHandle.CheckDigit);
        if (!verified)
        {
            throw new InvalidOperationException("Invalid handle: check digit verification failed.");
        }

        if (!_chubby.TryGetNode(clientHandle.Path, out (Node node, Status status) record))
        {
            throw new InvalidOperationException($"Invalid handle: node at path '{clientHandle.Path}' not found.");
        }

        if (record.status == Status.D)
        {
            throw new InvalidOperationException("Invalid handle: node has been deleted.");
        }

        if (record.node.Instance != clientHandle.InstanceNumber)
        {
            throw new InvalidOperationException("Invalid handle: node instance has changed. The node may have been deleted and recreated.");
        }

        var handle = _chubby.FindHandle(clientHandle.SessionId, clientHandle.HandleId);
        var session = _chubby.GetSession(clientHandle.SessionId);

        if (session.IsWaitingForAcknowledgmentForFailoverEvent())
        {
            throw new InvalidOperationException("Session is waiting for acknowledgment for failover event, cannot process any other requests.");
        }

        if (handle == null)
        {
            // Recreate the server-side handle. This can happen after a leader failover, as the
            // server-side Handle objects are volatile state.
            handle = new Handle(
                clientHandle.HandleId,
                clientHandle.Path,
                clientHandle.Permission,
                clientHandle.SubscribedEventsMask,
                clientHandle.InstanceNumber,
                clientHandle.SessionId,
                null // LockDelay is also lost.
            );
            _chubby.AddHandleToSession(handle);
            SessionScheduler.Remove(_chubby.GetSession(clientHandle.SessionId)); // since there is an open handle for this session, we don't need to track it.
        }
        return (record.node, handle);
    }

    private static HandleEventInterest? GetHandleEventInterest(Event @event)
    {
        return @event switch
        {
            LockAcquiredEvent => HandleEventInterest.LockAcquired,
            InvalidHandleAndLockEvent => HandleEventInterest.InvalidHandleAndLock,
            ConflictingLockRequestEvent => HandleEventInterest.ConflictingLockRequest,
            FileContentsModifiedEvent => HandleEventInterest.FileContentsModified,
            _ => null
        };
    }

    private async Task<bool> EnqueueHandleScopedEventIfSubscribed(Session session, Handle handle, Event @event)
    {
        var interest = GetHandleEventInterest(@event);
        if (interest is null || !handle.SubscribedEvents.HasFlag(interest.Value))
        {
            return false;
        }

        await session.channel.Writer.WriteAsync(@event);
        return true;
    }

    public async Task<bool> PublishHandleScopedEvent(string sessionId, string handleId, Event @event)
    {
        var session = _chubby.GetSession(sessionId);
        var handle = _chubby.FindHandle(sessionId, handleId);
        if (handle is null)
        {
            return false;
        }

        return await EnqueueHandleScopedEventIfSubscribed(session, handle, @event);
    }


    public async Task ProcessFailOver()
    {
        var sessions = _chubby.GetAllSessions();
        foreach (var session in sessions)
        {
            // this because last activity is not part of state machine, so we need to update it here, to prevent sessions from being expired immediately after failover. 
            session.UpdateLastActivity();
            await session.channel.Writer.WriteAsync(new MasterFailOverEvent { EpochNumber = CurrentEpochNumber() - 1 });
        }
        SessionScheduler.AddOrUpdateBulk(sessions);

        // fire and forget
        _ = ScheduleEphemeralNodeCleanup();
    }

    private async Task ScheduleEphemeralNodeCleanup()
    {
        await Task.Delay(_chubbyConfig.EphemeralNodeCleanupTimeout);
        await CleanUpEphemeralNodes();
    }

    // these need to go via Replication.
    private async Task CleanUpEphemeralNodes()
    {
        // clean up ephemeralNodes whose handles haven't been refreshed yet after failover.
        var nodesToCleanedUp = _chubby.nodes.Where((kvp) => kvp.Value.node.IsEphemeral).Select(kvp => kvp.Key);
        foreach (var ephemeralNodePath in nodesToCleanedUp)
        {
            if (_chubby.ShouldDeleteEphemeralNode(ephemeralNodePath))
            {
                var deleteNodeCmd = new DeleteNodeCommand { Path = ephemeralNodePath };
                var commandBytes = JsonSerializer.SerializeToUtf8Bytes(deleteNodeCmd as BaseCommand);
                var result = await _nodeEnvelope.WriteAsync(commandBytes);
            }
        }
    }


    public void StartSessionScheduler()
    {
        SessionScheduler.Start();
    }

    public void StopSessionScheduler()
    {
        SessionScheduler.Stop();
    }




    // SetSequencer
    // Will Come back to it, in next iteration.


    // on Handle Close, if session has no other handles, add it to sessionScheduler.
}
