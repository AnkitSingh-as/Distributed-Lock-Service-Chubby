using System.Collections.Concurrent;
using System.Text.Json;
using Raft;
using Chubby.Core.Model;
using Chubby.Core.Rpc;
using Chubby.Core.Sessions;
namespace Chubby.Core.StateMachine;
// should I create session tracker for every session that is created
// wait for 1 minute, using task.delay and check the condition
// or keep running a loop check the difference between last minutes
// which keeps on iterating on all the active session, 
// what is the efficient tradeoff?.



public partial class ChubbyCore : IStateMachine
{
    // This is the in-memory state of the Chubby cell, replicated via Raft.
    // Using ConcurrentDictionary for thread-safe access from gRPC service handlers.
    public readonly ConcurrentDictionary<string, (Node node, Status status)> nodes = new();
    private readonly ConcurrentDictionary<string, Session> sessionIdToSessionMapping = new();
    private readonly ConcurrentDictionary<string, Client> nameToClientMapping = new();
    public event Action<Session>? SessionClosed;
    public readonly ConcurrentDictionary<string, List<Lock>> nodePathToLockMapping = new();
    public readonly ConcurrentDictionary<Node, List<string>> nodeToSessionMapping = new();
    private readonly ILogger<ChubbyCore> _logger;
    private readonly ILoggerFactory _loggerFactory;

    public ChubbyCore(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<ChubbyCore>();
        _loggerFactory = loggerFactory;
    }

    public Session CreateSession(string sessionId, int leaseTimeout, Client client, int threshold)
    {
        var session = new Session(sessionId, leaseTimeout, client, _loggerFactory.CreateLogger<Session>(), threshold);
        if (sessionIdToSessionMapping.TryAdd(sessionId, session))
        {
            _logger.LogInformation("Created new session {SessionId} for client {ClientName}.", sessionId, client.Name);
            return session;
        }
        // This can happen if a command is retried after a timeout but before the response was received. It's safe to return the existing session.
        _logger.LogWarning("Session {SessionId} already existed. Returning existing session.", sessionId);
        return sessionIdToSessionMapping[sessionId];
    }
    public void CloseSession(string sessionId)
    {
        if (sessionIdToSessionMapping.TryRemove(sessionId, out var session))
        {
            _logger.LogInformation("Closing session {SessionId}.", sessionId);
            // Cancel any pending operations for this session, like the KeepAlive gRPC call, to release resources.
            session.cts.Cancel();

            // Cleanup ephemeral nodes. This is a state change and must be done inside the state machine.
            foreach (var ephemeralNodePath in session.pathToEphemeralNodesMapping.Keys)
            {
                CleanUpEphemeralNode(ephemeralNodePath);
            }

            // Release all locks held by the session.
            var locksToRemove = new List<(string path, Lock lockInstance)>();
            foreach (var kvp in nodePathToLockMapping)
            {
                var sessionLocks = kvp.Value.Where(l => l.SessionId == sessionId).ToList();
                foreach (var l in sessionLocks)
                {
                    locksToRemove.Add((kvp.Key, l));
                }
            }

            foreach (var (path, lockInstance) in locksToRemove)
            {
                if (nodePathToLockMapping.TryGetValue(path, out var locks))
                {
                    locks.Remove(lockInstance);
                    if (locks.Count == 0)
                    {
                        nodePathToLockMapping.TryRemove(path, out _);
                    }
                }
            }
            SessionClosed?.Invoke(session);
        }
    }

    private void CleanUpEphemeralNode(string ephemeralNodePath)
    {
        nodes.TryGetValue(ephemeralNodePath, out var ephemeralNodeRecord);
        var shouldDeleteEphemeralNode = ShouldDeleteEphemeralNode(ephemeralNodePath);
        if (shouldDeleteEphemeralNode)
        {
            nodes[ephemeralNodePath] = (ephemeralNodeRecord.node, Status.D);
        }

    }

    public bool ShouldDeleteEphemeralNode(string ephemeralNodePath)
    {
        nodes.TryGetValue(ephemeralNodePath, out var ephemeralNodeRecord);
        if (ephemeralNodeRecord.status == Status.C)
        {
            var sessionsForNode = nodeToSessionMapping.GetOrAdd(ephemeralNodeRecord.node, _ => new List<string>());
            var shouldDeleteEphemeralNode = true;
            foreach (var sessionPath in sessionsForNode)
            {
                sessionIdToSessionMapping.TryGetValue(sessionPath, out var sessionToCheck);
                if (sessionToCheck?.Handles.Count() != 0)
                {
                    shouldDeleteEphemeralNode = false;
                    break;
                }

            }
            return shouldDeleteEphemeralNode;
        }
        return false;
    }

    private bool ApplyOpen(OpenCommand cmd)
    {
        bool created = false;
        if (cmd.Create is null)
        {
            return created;
        }

        Node? createdNode = null;

        // This command is idempotent.
        if (nodes.TryGetValue(cmd.Path, out var record))
        {
            if (record.status == Status.D)
            {
                // Node was deleted, so we are creating a new instance of it.
                createdNode = Node.GenerateNextInstance(record.node, cmd.Create.Content, cmd.Create.WriteAcl,
                    cmd.Create.ReadAcl, cmd.Create.ChangeAcl, cmd.Create.IsEphemeral);
                nodes[cmd.Path] = (createdNode, Status.C);
                created = true;
            }
            // If node exists and status is not 'D', do nothing.
        }
        else
        {
            // Node does not exist, create it for the first time.
            createdNode = new Node(
                cmd.Path,
                cmd.Create.Content,
                cmd.Create.WriteAcl,
                cmd.Create.ReadAcl,
                cmd.Create.ChangeAcl,
                cmd.Create.IsEphemeral
            );
            nodes[cmd.Path] = (createdNode, Status.C);
            created = true;
        }

        // If an ephemeral node was created, associate it with the session.
        if (createdNode != null && cmd.Create.IsEphemeral)
        {
            if (sessionIdToSessionMapping.TryGetValue(cmd.SessionId, out var session))
            {
                session.pathToEphemeralNodesMapping.TryAdd(createdNode.name, createdNode);
                var list = nodeToSessionMapping.GetOrAdd(createdNode, _ => new List<string>());
                list.Add(cmd.SessionId);
            }
        }

        return created;
    }

    private void UpdateContent(SetContentsCommand cmd)
    {
        if (!nodes.TryGetValue(cmd.Path, out var record) || record.status == Status.D)
        {
            // log this
            return;
        }

        if (cmd.ContentGenerationNumber.HasValue && cmd.ContentGenerationNumber.Value != record.node.ContentGenerationNumber)
        {
            return;
        }
        var oldNode = record.node;
        var newNode = new Node(oldNode)
        {
            content = cmd.Content,
            ContentGenerationNumber = oldNode.ContentGenerationNumber + 1
        };
        nodes[cmd.Path] = (newNode, record.status);
    }

    private void UpdateAcl(SetAclCommand cmd)
    {
        if (!nodes.TryGetValue(cmd.Path, out var record) || record.status == Status.D)
        {
            return;
        }
        var oldNode = record.node;
        var newNode = new Node(oldNode)
        {
            WriteAcl = cmd.WriteAcl,
            ReadAcl = cmd.ReadAcl,
            ChangeAcl = cmd.ChangeAcl,
            AclGenerationNumber = oldNode.AclGenerationNumber + 1
        };
        nodes[cmd.Path] = (newNode, record.status);
    }

    private bool AcquireLock(AcquireLockCommand cmd)
    {
        var existingLocksOnNode = nodePathToLockMapping.GetOrAdd(cmd.Lock.Path, _ => new List<Lock>());

        var existingLockForHandle = existingLocksOnNode.FirstOrDefault(l => l.HandleId == cmd.Lock.HandleId);
        if (existingLockForHandle is not null)
        {
            // Re-acquiring the same lock on the same handle is idempotent.
            if (existingLockForHandle.LockType == cmd.Lock.LockType
                && existingLockForHandle.Instance == cmd.Lock.Instance)
            {
                return true;
            }

            return false;
        }

        if (existingLocksOnNode.Any())
        {
            // An exclusive lock is already held by someone.
            if (existingLocksOnNode.Any(l => l.LockType == LockType.Exclusive))
            {
                return false;
            }
            // Shared locks exist, and an exclusive lock is being requested.
            if (cmd.Lock.LockType == LockType.Exclusive)
            {
                return false;
            }
        }

        existingLocksOnNode.Add(cmd.Lock);

        if (nodes.TryGetValue(cmd.Lock.Path, out var record) && record.status != Status.D)
        {
            var oldNode = record.node;
            var newNode = new Node(oldNode)
            {
                LockGenerationNumber = oldNode.LockGenerationNumber + 1
            };
            nodes[cmd.Lock.Path] = (newNode, record.status);
        }
        return true;
    }

    private bool ReleaseLock(ReleaseLockCommand cmd)
    {
        bool released = false;
        if (nodePathToLockMapping.TryGetValue(cmd.Lock.Path, out var existingLocksOnNode))
        {
            var lockToRemove = existingLocksOnNode.FirstOrDefault(l =>
                l.HandleId == cmd.Lock.HandleId &&
                l.LockType == cmd.Lock.LockType &&
                l.Instance == cmd.Lock.Instance);

            if (lockToRemove != null)
            {
                existingLocksOnNode.Remove(lockToRemove);
                if (existingLocksOnNode.Count == 0)
                {
                    nodePathToLockMapping.TryRemove(cmd.Lock.Path, out _);
                }
                released = true;
            }
        }
        return released;
    }


    private bool DeleteNode(DeleteNodeCommand cmd)
    {
        if (GetImmediateChildRelativePaths(cmd.Path).Any())
        {
            return false; // Cannot delete a node that has children.
        }

        if (nodes.TryGetValue(cmd.Path, out var record) && record.status != Status.D)
        {
            nodes[cmd.Path] = (record.node, Status.D);
            return true;
        }
        return false;
    }
    public object? Apply(byte[] commandBytes)
    {
        // A no-op entry is represented by an empty command array. It's used by a new leader
        // to commit entries from previous terms. The state machine just ignores it.
        if (commandBytes == null || commandBytes.Length == 0)
        {
            return null;
        }

        var command = JsonSerializer.Deserialize<BaseCommand>(commandBytes);

        switch (command)
        {
            case CreateSessionCommand createSessionCmd:
                var client = nameToClientMapping.GetOrAdd(createSessionCmd.Client.Name, (name) => new Client { Name = name });
                CreateSession(createSessionCmd.SessionId, createSessionCmd.LeaseTimeout, client, createSessionCmd.Threshold);
                return null;
            case CloseSessionCommand closeSessionCmd:
                CloseSession(closeSessionCmd.SessionId);
                return null;
            case OpenCommand openCmd:
                return ApplyOpen(openCmd);
            case SetContentsCommand setContentsCmd:
                UpdateContent(setContentsCmd);
                return null;
            case SetAclCommand setAclCmd:
                UpdateAcl(setAclCmd);
                return null;
            case AcquireLockCommand acquireLockCmd:
                return AcquireLock(acquireLockCmd);
            case ReleaseLockCommand releaseLockCmd:
                return ReleaseLock(releaseLockCmd);
            case DeleteNodeCommand deleteNodeCmd:
                return DeleteNode(deleteNodeCmd);
            default:
                return null;
        }
    }
}

