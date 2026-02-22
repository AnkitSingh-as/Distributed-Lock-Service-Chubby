using System.Collections.Immutable;
using System.Collections.Generic;
using System.Linq;
using Chubby.Core.Model;
using System;
using Chubby.Core.Sessions;

namespace Chubby.Core.StateMachine;

public partial class ChubbyCore
{
    public bool TryGetNode(string path, out (Node node, Status status) record)
    {
        return nodes.TryGetValue(path, out record);
    }
    public List<Node> GetNodesStartingWith(string path)
    {
        return nodes.ToImmutableList().Where((p) => p.Key.StartsWith(path) && p.Value.status != Status.D).Select(p => p.Value.node).ToList();
    }


    // can be optimized with
    public string[] GetChildNodeNames(string parentPath)
    {
        // Use the centralized helper method.
        return GetImmediateChildRelativePaths(parentPath).ToArray();
    }

    private IEnumerable<string> GetImmediateChildRelativePaths(string parentPath)
    {
        var dirPath = parentPath.EndsWith("/") ? parentPath : parentPath + "/";

        foreach (var kvp in nodes)
        {
            if (kvp.Value.status == Status.D)
            {
                continue;
            }

            if (kvp.Key.StartsWith(dirPath) && kvp.Key.Length > dirPath.Length)
            {
                var relativePath = kvp.Key.Substring(dirPath.Length);
                if (!relativePath.Contains('/'))
                {
                    yield return relativePath;
                }
            }
        }
    }


    public Handle? FindHandle(string sessionId, string handleId)
    {
        if (sessionIdToSessionMapping.TryGetValue(sessionId, out var session))
        {
            if (session.Handles.TryGetValue(handleId, out var handle))
            {
                return handle;
            }
        }
        return null;
    }

    public string? GetNodeSequencer(Node node)
    {
        if (nodePathToLockMapping.TryGetValue(node.name, out var existingLocksOnNode) && existingLocksOnNode.Any())
        {
            return BuildNodeSequencer(node, existingLocksOnNode[0].LockType);
        }
        return null;
    }

    public bool CheckNodeSequencer(Node node, string sequencer)
    {
        if (nodePathToLockMapping.TryGetValue(node.name, out var existingLocksOnNode) && existingLocksOnNode.Any())
        {
            if (existingLocksOnNode[0].LockType != LockType.Exclusive)
            {
                return false;
            }
            var currentSequencer = BuildNodeSequencer(node, existingLocksOnNode[0].LockType);
            return currentSequencer == sequencer;
        }
        return false;
    }

    private static string BuildNodeSequencer(Node node, LockType lockType)
    {
        return node.name + lockType + node.LockGenerationNumber;
    }

    public Session GetSession(string sessionId)
    {
        return sessionIdToSessionMapping[sessionId];
    }

    public void AddHandleToSession(Handle handle)
    {
        if (!sessionIdToSessionMapping.TryGetValue(handle.SessionId, out var session))
        {
            throw new InvalidOperationException($"Session '{handle.SessionId}' not found.");
        }
        session.AddHandleToSession(handle);
    }

    public IEnumerable<Session> GetAllSessions()
    {
        return sessionIdToSessionMapping.Values;
    }

}

