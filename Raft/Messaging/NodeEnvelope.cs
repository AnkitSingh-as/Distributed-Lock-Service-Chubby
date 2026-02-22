using Microsoft.Extensions.Logging;

namespace Raft;

public class NodeEnvelope : IServer, INodeEnvelope
{
    private readonly ILogger<NodeEnvelope> _logger;
    public RaftNode Node { get; }
    private readonly RaftConfig _config;
    public IStatePersister StatePersister { get; }
    private readonly ElectionTimer _electionTimer;
    private readonly HeartBeatTimer _heartbeatTimer;
    private CancellationTokenSource electionTimerCts;
    private CancellationTokenSource heartbeatCts;
    public IDictionary<int, IServer> Peers { get; set; }
    private readonly FollowerReplicator[] _followerReplicators;
    private readonly VoteRequester[] _voteRequesters;
    private bool hasThisNodeChangedToLeader = false;
    public event Action? AllEntriesCommittedOnTransitionToLeader;
    public event Action<Role, Role>? RoleChanged;
    private bool onAllEntriesCommittedOnTransitionToLeaderEventGenerated = false;

    private readonly IStateMachine _stateMachine;
    public ActivityTracker ActivityTracker { get; }
    private NodeEnvelope(IDataSource dataSource, RaftConfig config, int id, IStateMachine stateMachine, ActivityTracker raftClock, ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<NodeEnvelope>();
        _config = config;
        _stateMachine = stateMachine;
        StatePersister = new StatePersister(dataSource, this, loggerFactory.CreateLogger<StatePersister>());
        ActivityTracker = raftClock;
        Node = new RaftNode(config, new State(), this, id, _stateMachine, loggerFactory.CreateLogger<RaftNode>());
        _electionTimer = new ElectionTimer(Node, raftClock, loggerFactory.CreateLogger<ElectionTimer>());
        _heartbeatTimer = new HeartBeatTimer(Node, raftClock, loggerFactory.CreateLogger<HeartBeatTimer>());
        this.electionTimerCts = new CancellationTokenSource();
        this.heartbeatCts = new CancellationTokenSource();

        _followerReplicators = new FollowerReplicator[config.ClusterSize];
        _voteRequesters = new VoteRequester[config.ClusterSize];
        for (int i = 0; i < config.ClusterSize; i++)
        {
            // The workers for the node's own ID will be created but never signaled.
            _followerReplicators[i] = new FollowerReplicator(i, Node, this, loggerFactory);
            _voteRequesters[i] = new VoteRequester(i, Node, this, loggerFactory);
        }
        _logger.LogDebug("NodeEnvelope for Node {NodeId} created.", id);
    }

    public static async Task<NodeEnvelope> CreateAsync(IDataSource dataSource, RaftConfig config, int id, IStateMachine stateMachine, IRaftClock raftClock, ILoggerFactory loggerFactory)
    {
        var clock = new ActivityTracker(raftClock);
        var envelope = new NodeEnvelope(dataSource, config, id, stateMachine, clock, loggerFactory);
        await envelope.InitializeStateAsync();
        envelope.Start();
        return envelope;
    }

    public async Task InitializeStateAsync()
    {
        _logger.LogDebug("Node {NodeId} initializing state from data source.", Node.Id);
        var persistentState = await StatePersister.LoadStateAsync();
        Node.State.CurrentTerm = persistentState.CurrentTerm;
        Node.State.VotedFor = persistentState.VotedFor;
        Node.State.Logs = persistentState.Logs;
        // to do state machine recreation when all nodes restarted.
        Node.InitializeDurableState();
        _logger.LogDebug("Node {NodeId} state initialized. Term: {Term}, VotedFor: {VotedFor}, Log Count: {LogCount}", Node.Id, Node.State.CurrentTerm, Node.State.VotedFor, Node.State.Logs.Count);
    }

    public void Start()
    {
        // Node starts as a follower, so start the election timer.
        ResetElectionTimer();
        // Start the main event processing loop.
        _ = StartEventProcessingAsync();
        _logger.LogInformation("Node {NodeId} started.", Node.Id);
    }

    private async Task StartEventProcessingAsync()
    {
        _logger.LogDebug("Node {NodeId} starting event processing loop.", Node.Id);
        await foreach (var raftEvent in Node.EventChannel.Reader.ReadAllAsync())
        {
            _logger.LogTrace("Node {NodeId} processing event {EventType}.", Node.Id, raftEvent.GetType().Name);
            var roleBefore = Node.CurrentRole;
            raftEvent.Apply(Node);
            var roleAfter = Node.CurrentRole;

            if (roleBefore != roleAfter)
            {
                RoleChanged?.Invoke(roleBefore, roleAfter);
                HandleRoleChange(roleBefore, roleAfter);
            }
        }
        _logger.LogDebug("Node {NodeId} event processing loop stopped.", Node.Id);
    }

    private void HandleRoleChange(Role oldRole, Role newRole)
    {
        _logger.LogInformation("Node {NodeId} role changed from {OldRole} to {NewRole}.", Node.Id, oldRole, newRole);
        electionTimerCts.Cancel();
        heartbeatCts.Cancel();

        if (newRole == Role.Follower || newRole == Role.Candidate)
        {
            _logger.LogTrace("Node {NodeId} resetting election timer due to role change.", Node.Id);
            ResetElectionTimer();
            onAllEntriesCommittedOnTransitionToLeaderEventGenerated = false;
            hasThisNodeChangedToLeader = false;
        }
        else if (newRole == Role.Leader)
        {
            _logger.LogTrace("Node {NodeId} resetting heartbeat timer due to role change.", Node.Id);
            ResetHeartbeatTimer();
            hasThisNodeChangedToLeader = true;
        }
    }

    private void ResetElectionTimer()
    {
        _logger.LogDebug("Node {NodeId} resetting election timer.", Node.Id);
        electionTimerCts = new CancellationTokenSource();
        _ = _electionTimer.RunAsync(electionTimerCts.Token, _config.ElectionTimeoutMinMs, _config.ElectionTimeoutMaxMs);
    }

    private void ResetHeartbeatTimer()
    {
        _logger.LogDebug("Node {NodeId} resetting heartbeat timer.", Node.Id);
        heartbeatCts = new CancellationTokenSource();
        _ = _heartbeatTimer.RunAsync(heartbeatCts.Token, _config.HeartbeatIntervalMs);
    }

    public async Task<AppendEntryResponse> AppendEntries(int term, int leaderId, int prevLogIndex, int prevLogTerm, List<Log> entries, int leaderCommit)
    {
        _logger.LogTrace("Node {NodeId} received AppendEntries RPC from {LeaderId}. Term: {Term}, PrevLogIndex: {PrevLogIndex}, PrevLogTerm: {PrevLogTerm}, EntryCount: {EntryCount}. Enqueuing event.",
            Node.Id, leaderId, term, prevLogIndex, prevLogTerm, entries.Count);
        var raftEvent = new AppendEntriesEvent(term, leaderId, prevLogIndex, prevLogTerm, entries, leaderCommit);
        Node.enqueueEvent(raftEvent);
        var result = await raftEvent.tcs.Task;
        if (result.Ack is not null)
        {
            _logger.LogTrace("Node {NodeId} waiting for AppendEntries event to be acknowledged.", Node.Id);
            await result.Ack.Task;
        }
        return result;
    }

    public async Task<RequestVoteResponse> RequestVote(int term, int candidateId, int lastLogIndex, int lastLogTerm)
    {
        _logger.LogTrace("Node {NodeId} received RequestVote RPC from {CandidateId}. Term: {Term}, LastLogIndex: {LastLogIndex}, LastLogTerm: {LastLogTerm}. Enqueuing event.",
            Node.Id, candidateId, term, lastLogIndex, lastLogTerm);
        var raftEvent = new RequestVoteEvent(term, candidateId, lastLogIndex, lastLogTerm);
        Node.enqueueEvent(raftEvent);
        var result = await raftEvent.tcs.Task;
        if (result.Ack is not null)
        {
            _logger.LogTrace("Node {NodeId} waiting for RequestVote event to be acknowledged.", Node.Id);
            await result.Ack.Task;
        }
        return result;
    }

    public async Task<object?> WriteAsync(byte[] command)
    {
        _logger.LogTrace("Node {NodeId} enqueuing client write request.", Node.Id);
        var tcs = RaftTcs.Create<object?>();
        var raftEvent = new WriteEvent(command, tcs);
        Node.enqueueEvent(raftEvent);
        return await tcs.Task;
    }

    internal async Task<AppendEntryResponse> AppendEntriesAsync(int followerId, AppendEntriesArgs args)
    {
        if (Peers.TryGetValue(followerId, out var peer))
        {
            _logger.LogTrace("Node {NodeId} sending AppendEntries RPC to Peer {PeerId}", Node.Id, followerId);
            return await peer.AppendEntries(args.Term, args.LeaderId, args.PrevLogIndex, args.PrevLogTerm, args.Entries, args.LeaderCommit);
        }

        _logger.LogError("Node {NodeId} failed to find Peer {PeerId} for AppendEntries RPC.", Node.Id, followerId);
        throw new InvalidOperationException($"Peer with ID {followerId} not found.");
    }

    internal async Task<RequestVoteResponse> RequestVoteAsync(int peerId, RequestVoteSnapshot requestVoteArgs)
    {
        if (Peers.TryGetValue(peerId, out var peer))
        {
            _logger.LogTrace("Node {NodeId} sending RequestVote RPC to Peer {PeerId}", Node.Id, peerId);
            return await peer.RequestVote(requestVoteArgs.Term, requestVoteArgs.CandidateId, requestVoteArgs.LastLogIndex, requestVoteArgs.LastLogTerm);
        }

        // This should not happen in a correctly configured cluster.
        _logger.LogError("Node {NodeId} failed to find Peer {PeerId} for RequestVote RPC.", Node.Id, peerId);
        throw new InvalidOperationException($"Peer with ID {peerId} not found.");
    }

    public void SignalAllReplicators()
    {
        _logger.LogTrace("Node {NodeId} signaling all follower replicators.", Node.Id);
        for (int i = 0; i < _followerReplicators.Length; i++)
        {
            if (i == Node.Id) continue;
            _followerReplicators[i].Signal();
        }
    }

    public void SignalReplicator(int followerId)
    {
        _logger.LogTrace("Node {NodeId} signaling follower replicator for Follower {FollowerId}.", Node.Id, followerId);
        _followerReplicators[followerId].Signal();
    }

    public void SignalAllVoteRequesters()
    {
        _logger.LogTrace("Node {NodeId} signaling all vote requesters.", Node.Id);
        for (int i = 0; i < _voteRequesters.Length; i++)
        {
            if (i == Node.Id) continue;
            _voteRequesters[i].Signal();
        }
    }

    public void AllEntriesCommitted()
    {
        if (hasThisNodeChangedToLeader && !onAllEntriesCommittedOnTransitionToLeaderEventGenerated)
        {
            AllEntriesCommittedOnTransitionToLeader?.Invoke();
            onAllEntriesCommittedOnTransitionToLeaderEventGenerated = true;
        }
    }

    public void Shutdown()
    {
        _logger.LogInformation("Node {NodeId} shutting down.", Node.Id);
        foreach (var replicator in _followerReplicators)
        {
            replicator.Stop();
        }
        electionTimerCts.Cancel();
        heartbeatCts.Cancel();
        this.Node.EventChannel.Writer.TryComplete();
        foreach (var replicator in _followerReplicators)
        {
            replicator.Dispose();
        }
        electionTimerCts.Dispose();
        heartbeatCts.Dispose();
    }

    public int CurrentEpochNumber()
    {
        return Node.State.CurrentTerm;
    }

    public bool IsLeader()
    {
        return Node.CurrentRole == Role.Leader;
    }

}