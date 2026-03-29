using Chubby.Protos;

public sealed class ClientCacheState
{
    public HashSet<(ClientHandle Handle, LockType LockType)> lockCache;
    public Dictionary<OpenRequest, ClientHandle> requestHandleCache;
    public Dictionary<string, Node> nodeCache;
    public Dictionary<string, OpenRequest> handleIdToRequestCache;
    public Dictionary<string, ClientHandle> handleIdToHandleCache;
    public Dictionary<string, HashSet<string>> pathToHandleIdsCache;

    public ClientCacheState()
    {
        lockCache = new();
        requestHandleCache = new(new OpenRequestComparer());
        nodeCache = new(StringComparer.Ordinal);
        handleIdToRequestCache = new(StringComparer.Ordinal);
        handleIdToHandleCache = new(StringComparer.Ordinal);
        pathToHandleIdsCache = new(StringComparer.Ordinal);
    }

    public void CacheHandle(OpenRequest request, ClientHandle handle)
    {
        requestHandleCache[request.Clone()] = handle;
        handleIdToRequestCache[handle.HandleId] = request.Clone();
        handleIdToHandleCache[handle.HandleId] = handle;

        if (!pathToHandleIdsCache.TryGetValue(handle.Path, out var handleIds))
        {
            handleIds = new HashSet<string>(StringComparer.Ordinal);
            pathToHandleIdsCache[handle.Path] = handleIds;
        }

        handleIds.Add(handle.HandleId);
    }

    public void CacheNode(Node node)
    {
        nodeCache[node.Path] = node;
    }

    public void InvalidateContents(string path, long instanceNumber, long contentGenerationNumber, string handleId)
    {
        if (!nodeCache.TryGetValue(path, out var cachedNode))
        {
            return;
        }

        if (cachedNode.Stat.InstanceNumber != instanceNumber)
        {
            nodeCache.Remove(path);
            return;
        }

        if (cachedNode.Stat.ContentGenerationNumber < contentGenerationNumber)
        {
            nodeCache.Remove(path);
        }
    }

    public void InvalidateHandleAndLock(string handleId)
    {
        if (handleIdToHandleCache.TryGetValue(handleId, out var handle))
        {
            requestHandleCache.Remove(handleIdToRequestCache[handleId]);
            handleIdToHandleCache.Remove(handleId);
            handleIdToRequestCache.Remove(handleId);

            if (pathToHandleIdsCache.TryGetValue(handle.Path, out var handleIds))
            {
                handleIds.Remove(handleId);
                if (handleIds.Count == 0)
                {
                    pathToHandleIdsCache.Remove(handle.Path);
                }
            }
        }

        lockCache.RemoveWhere(entry => string.Equals(entry.Handle.HandleId, handleId, StringComparison.Ordinal));
    }

    public void RecordConflictingLockRequest(string handleId, string path, long instanceNumber, LockType lockType)
    {
        // The conflicting lock event in theory
        // permits clients to cache data held on other servers, using
        // Chubby locks to maintain cache consistency. A notification of a conflicting lock request would tell a client to
        // finish using data associated with the lock: it would finish
        // pending operations, flush modifications to a home locations, discard and release.

        // notify client about events.
    }

    public void RecordLockAcquired(string handleId, string path, long instanceNumber, LockType lockType)
    {
        if (!handleIdToHandleCache.TryGetValue(handleId, out var handle))
        {
            return;
        }

        lockCache.RemoveWhere(entry =>
            string.Equals(entry.Handle.Path, path, StringComparison.Ordinal)
            && entry.Handle.InstanceNumber == instanceNumber);

        lockCache.Add((handle, lockType));
    }

    public void InvalidateAllForFailover(int epochNumber)
    {
        lockCache.Clear();
        requestHandleCache.Clear();
        nodeCache.Clear();
        handleIdToRequestCache.Clear();
        handleIdToHandleCache.Clear();
        pathToHandleIdsCache.Clear();
    }
}

