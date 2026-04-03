using Chubby.Protos;
using Google.Protobuf;

public sealed class ClientCacheState
{
    private readonly HashSet<(ClientHandle Handle, LockType LockType)> _lockCache;
    private readonly Dictionary<OpenRequest, ClientHandle> _requestHandleCache;
    private readonly Dictionary<string, Node> _nodeCache;
    private readonly Dictionary<string, OpenRequest> _handleIdToRequestCache;
    private readonly Dictionary<string, ClientHandle> _handleIdToHandleCache;
    private readonly Dictionary<string, HashSet<string>> _pathToHandleIdsCache;

    public ClientCacheState()
    {
        _lockCache = new();
        _requestHandleCache = new(new OpenRequestComparer());
        _nodeCache = new(StringComparer.Ordinal);
        _handleIdToRequestCache = new(StringComparer.Ordinal);
        _handleIdToHandleCache = new(StringComparer.Ordinal);
        _pathToHandleIdsCache = new(StringComparer.Ordinal);
    }

    public bool TryGetCachedHandle(OpenRequest request, out ClientHandle handle)
    {
        if (_requestHandleCache.TryGetValue(request, out var cachedHandle))
        {
            handle = cachedHandle.Clone();
            return true;
        }

        handle = new ClientHandle();
        return false;
    }

    public void CacheHandle(OpenRequest request, ClientHandle handle)
    {
        var requestCopy = request.Clone();
        var handleCopy = handle.Clone();

        _requestHandleCache[requestCopy] = handleCopy;
        _handleIdToRequestCache[handleCopy.HandleId] = requestCopy;
        _handleIdToHandleCache[handleCopy.HandleId] = handleCopy;

        if (!_pathToHandleIdsCache.TryGetValue(handleCopy.Path, out var handleIds))
        {
            handleIds = new HashSet<string>(StringComparer.Ordinal);
            _pathToHandleIdsCache[handleCopy.Path] = handleIds;
        }

        handleIds.Add(handleCopy.HandleId);
    }

    public bool TryGetCachedContentsAndStat(ClientHandle handle, out GetContentsAndStatResponse response)
    {
        if (TryGetCachedNode(handle, requireContent: true, out var cachedNode))
        {
            response = new GetContentsAndStatResponse
            {
                Content = ByteString.CopyFrom(cachedNode.Content!),
                Stat = cachedNode.Stat.Clone(),
                IsCacheable = true
            };
            return true;
        }

        response = new GetContentsAndStatResponse();
        return false;
    }

    public bool TryGetCachedStat(ClientHandle handle, out GetStatResponse response)
    {
        if (TryGetCachedNode(handle, requireContent: false, out var cachedNode))
        {
            response = new GetStatResponse
            {
                Stat = cachedNode.Stat.Clone(),
                IsCacheable = true
            };
            return true;
        }

        response = new GetStatResponse();
        return false;
    }

    public void CacheContentsAndStat(ClientHandle handle, ByteString content, Stat stat)
    {
        if (!IsHandleTracked(handle))
        {
            return;
        }

        if (stat.InstanceNumber != handle.InstanceNumber)
        {
            InvalidatePathAndAssociatedState(handle.Path);
            return;
        }

        _nodeCache[handle.Path] = new Node
        {
            Path = handle.Path,
            Content = content.ToByteArray(),
            Stat = stat.Clone()
        };
    }

    public void CacheStat(ClientHandle handle, Stat stat)
    {
        if (!IsHandleTracked(handle))
        {
            return;
        }

        if (stat.InstanceNumber != handle.InstanceNumber)
        {
            InvalidatePathAndAssociatedState(handle.Path);
            return;
        }

        _nodeCache[handle.Path] = new Node
        {
            Path = handle.Path,
            Content = _nodeCache.TryGetValue(handle.Path, out var existingNode) ? existingNode.Content?.ToArray() : null,
            Stat = stat.Clone()
        };
    }

    public void InvalidatePath(string path)
    {
        _nodeCache.Remove(path);
    }

    public void InvalidatePathAndAssociatedState(string path)
    {
        _nodeCache.Remove(path);

        if (!_pathToHandleIdsCache.TryGetValue(path, out var handleIds))
        {
            return;
        }

        foreach (var handleId in handleIds.ToList())
        {
            if (_handleIdToRequestCache.TryGetValue(handleId, out var request))
            {
                _requestHandleCache.Remove(request);
                _handleIdToRequestCache.Remove(handleId);
            }

            _handleIdToHandleCache.Remove(handleId);
            _lockCache.RemoveWhere(entry => string.Equals(entry.Handle.HandleId, handleId, StringComparison.Ordinal));
        }

        _pathToHandleIdsCache.Remove(path);
    }

    public void InvalidateContents(string path, long instanceNumber, long contentGenerationNumber)
    {
        if (!_nodeCache.TryGetValue(path, out var cachedNode))
        {
            return;
        }

        if (cachedNode.Stat.InstanceNumber != instanceNumber)
        {
            InvalidatePathAndAssociatedState(path);
            return;
        }

        if (cachedNode.Stat.ContentGenerationNumber < contentGenerationNumber)
        {
            _nodeCache.Remove(path);
        }
    }

    public void InvalidateHandleAndLock(string handleId)
    {
        if (_handleIdToHandleCache.TryGetValue(handleId, out var handle))
        {
            if (_handleIdToRequestCache.TryGetValue(handleId, out var request))
            {
                _requestHandleCache.Remove(request);
                _handleIdToRequestCache.Remove(handleId);
            }

            _handleIdToHandleCache.Remove(handleId);

            if (_pathToHandleIdsCache.TryGetValue(handle.Path, out var handleIds))
            {
                handleIds.Remove(handleId);
                if (handleIds.Count == 0)
                {
                    _pathToHandleIdsCache.Remove(handle.Path);
                    _nodeCache.Remove(handle.Path);
                }
            }
        }

        _lockCache.RemoveWhere(entry => string.Equals(entry.Handle.HandleId, handleId, StringComparison.Ordinal));
    }

    public void RecordConflictingLockRequest(string path, long instanceNumber, LockType lockType)
    {
        InvalidatePath(path);
    }

    public bool HasLock(string handleId, LockType lockType)
    {
        return _lockCache.Any(entry =>
            string.Equals(entry.Handle.HandleId, handleId, StringComparison.Ordinal)
            && entry.LockType == lockType);
    }

    public void RecordLockAcquired(string handleId, string path, long instanceNumber, LockType lockType)
    {
        if (!_handleIdToHandleCache.TryGetValue(handleId, out var handle))
        {
            InvalidatePath(path);
            return;
        }

        InvalidatePath(path);

        _lockCache.RemoveWhere(entry =>
            string.Equals(entry.Handle.HandleId, handleId, StringComparison.Ordinal));

        if (lockType == LockType.Exclusive)
        {
            _lockCache.RemoveWhere(entry =>
                string.Equals(entry.Handle.Path, path, StringComparison.Ordinal)
                && entry.Handle.InstanceNumber == instanceNumber);
        }
        else
        {
            _lockCache.RemoveWhere(entry =>
                string.Equals(entry.Handle.Path, path, StringComparison.Ordinal)
                && entry.Handle.InstanceNumber == instanceNumber
                && entry.LockType == LockType.Exclusive);
        }

        _lockCache.Add((handle.Clone(), lockType));
    }

    public void RecordLockReleased(string handleId)
    {
        if (_handleIdToHandleCache.TryGetValue(handleId, out var handle))
        {
            InvalidatePath(handle.Path);
        }

        _lockCache.RemoveWhere(entry => string.Equals(entry.Handle.HandleId, handleId, StringComparison.Ordinal));
    }

    public void FlushCachesForFailover()
    {
        _nodeCache.Clear();
        _requestHandleCache.Clear();
        _handleIdToRequestCache.Clear();
    }

    public void ClearAll()
    {
        _lockCache.Clear();
        _requestHandleCache.Clear();
        _nodeCache.Clear();
        _handleIdToRequestCache.Clear();
        _handleIdToHandleCache.Clear();
        _pathToHandleIdsCache.Clear();
    }

    private bool TryGetCachedNode(ClientHandle handle, bool requireContent, out Node node)
    {
        if (!IsHandleTracked(handle))
        {
            node = null!;
            return false;
        }

        if (!_nodeCache.TryGetValue(handle.Path, out var cachedNode))
        {
            node = null!;
            return false;
        }

        if (cachedNode.Stat.InstanceNumber != handle.InstanceNumber)
        {
            InvalidatePathAndAssociatedState(handle.Path);
            node = null!;
            return false;
        }

        if (requireContent && cachedNode.Content is null)
        {
            node = null!;
            return false;
        }

        node = new Node
        {
            Path = cachedNode.Path,
            Content = cachedNode.Content?.ToArray(),
            Stat = cachedNode.Stat.Clone()
        };
        return true;
    }

    private bool IsHandleTracked(ClientHandle handle)
    {
        return _handleIdToHandleCache.TryGetValue(handle.HandleId, out var trackedHandle)
            && string.Equals(trackedHandle.Path, handle.Path, StringComparison.Ordinal)
            && trackedHandle.InstanceNumber == handle.InstanceNumber;
    }
}
