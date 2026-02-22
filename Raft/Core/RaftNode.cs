using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Raft;
public class RaftNode
{
    private readonly ILogger<RaftNode> _logger;
    public State State { get; }
    public IStatePersister statePersister { get; }
    private readonly IStateMachine _stateMachine;
    public Role CurrentRole { get; set; }
    private readonly HashSet<int> _durable = new();
    private HashSet<int> _votesReceived = new();

    public int Id { get; private set; }
    private readonly INodeEnvelope _nodeEnvelope;
    private readonly Dictionary<int, TaskCompletionSource<object?>> _pendingClients = new();
    internal Channel<IRaftEvent> EventChannel = Channel.CreateUnbounded<IRaftEvent>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
        }
    );
    private readonly RaftConfig _configuration;
    public RaftNode(RaftConfig config, State state, INodeEnvelope nodeEnvelope, int id, IStateMachine stateMachine, ILogger<RaftNode> logger)
    {
        _configuration = config;
        State = state;
        statePersister = nodeEnvelope.StatePersister;
        _nodeEnvelope = nodeEnvelope;
        Id = id;
        _stateMachine = stateMachine;
        _logger = logger;
        _logger.LogDebug("RaftNode {NodeId} created. Initial Role: {Role}, Initial Term: {Term}", Id, CurrentRole, State.CurrentTerm);
    }
    public void enqueueEvent(IRaftEvent raftEvent)
    {
        EventChannel.Writer.TryWrite(raftEvent);
    }

    internal void InitializeDurableState()
    {
        // When state is loaded from persistent storage, all loaded log entries
        // are by definition durable on this node. This populates the durable set
        // on startup.
        _durable.Clear();
        foreach (var log in State.Logs)
        {
            _durable.Add(log.Index);
        }
        _logger.LogInformation("Node {NodeId} initialized durable set with {Count} entries from persisted log.", Id, _durable.Count);
    }

    // this looks fine , until now.
    internal RequestVoteResponse HandleRequestVote(int term, int candidateId, int lastLogIndex, int lastLogTerm)
    {
        _logger.LogDebug("Node {NodeId} received RequestVote from Candidate {CandidateId} at Term {Term}.", Id, candidateId, term);

        var response = new RequestVoteResponse
        {
            Term = State.CurrentTerm,
            VoteGranted = false
        };

        if (term < State.CurrentTerm)
        {
            _logger.LogWarning("Node {NodeId} rejected vote for Candidate {CandidateId}: Stale Term {Term} < {CurrentTerm}", Id, candidateId, term, State.CurrentTerm);
            return response;
        }

        bool stateChanged = false;
        if (term > State.CurrentTerm)
        {
            BecomeFollower(term);
            stateChanged = true;
        }

        var myLastLog = State.Logs.LastOrDefault();
        var myLastLogTerm = myLastLog?.Term ?? 0; // Term of log at 0 index is 0
        var myLastLogIndex = myLastLog?.Index ?? 0; // Index of log at 0 index is 0

        _logger.LogTrace("Node {NodeId} checking log up-to-dateness. Candidate's Last: (Term: {CandidateTerm}, Index: {CandidateIndex}), My Last: (Term: {MyTerm}, Index: {MyIndex})", Id, lastLogTerm, lastLogIndex, myLastLogTerm, myLastLogIndex);
        var logUpToDate = (lastLogTerm > myLastLogTerm) ||
                          (lastLogTerm == myLastLogTerm && lastLogIndex >= myLastLogIndex);

        if ((State.VotedFor == null || State.VotedFor == candidateId) && logUpToDate)
        {
            // Grant vote and reset election timer.
            _nodeEnvelope.ActivityTracker.LastActivityTicks = _nodeEnvelope.ActivityTracker.Now;
            State.VotedFor = candidateId;
            response.VoteGranted = true;
            stateChanged = true;
            _logger.LogDebug("Node {NodeId} granted vote for Candidate {CandidateId} in Term {Term}", Id, candidateId, State.CurrentTerm);
        }
        else
        {
            _logger.LogDebug("Node {NodeId} denied vote for Candidate {CandidateId} in Term {Term}. Log up-to-date: {LogUpToDate}, VotedFor: {VotedFor}", Id, candidateId, State.CurrentTerm, logUpToDate, State.VotedFor);
        }

        response.Term = State.CurrentTerm;

        if (stateChanged)
        {
            response.WithAck();
        }

        return response;
    }


    // this too looks fine. 
    internal AppendEntryResponse HandleAppendEntries(int term, int leaderId, int prevLogIndex, int prevLogTerm, List<Log> entries, int leaderCommit)
    {
        var response = new AppendEntryResponse
        {
            Term = State.CurrentTerm,
            Success = false
        };
        _logger.LogTrace("Node {NodeId} received AppendEntries from Leader {LeaderId} at Term {Term}. Entries count: {EntriesCount}", Id, leaderId, term, entries.Count);


        if (term < State.CurrentTerm)
        {
            _logger.LogWarning("Node {NodeId} rejected AppendEntries from Leader {LeaderId}: Stale Term {Term} < {CurrentTerm}", Id, leaderId, term, State.CurrentTerm);
            return response;
        }

        // Rules for all servers, point 2.
        if (term > State.CurrentTerm)
        {
            BecomeFollower(term);
        }


        if (CurrentRole == Role.Candidate && term == State.CurrentTerm)
        {
            BecomeFollower(term);
        }

        var clock = _nodeEnvelope.ActivityTracker;

        if (term >= State.CurrentTerm)
        {
            clock.LastActivityTicks = clock.Now;
        }

        // Rule 2: Reply false if log doesn’t contain an entry at prevLogIndex whose term matches prevLogTerm.
        // leader will decrement its nextIndex for that follower and retry
        if (prevLogIndex > State.Logs.Count || (prevLogIndex > 0 && State.Logs[prevLogIndex - 1].Term != prevLogTerm))
        {
            _logger.LogWarning("Node {NodeId} rejected AppendEntries from Leader {LeaderId}: Log consistency check failed at PrevLogIndex {PrevLogIndex}", Id, leaderId, prevLogIndex);
            response.Term = State.CurrentTerm;
            return response;
        }

        // Find any existing entries that conflict with the new ones and truncate the log.
        var newEntries = entries.Where(e => e.Index > State.Logs.Count || State.Logs[e.Index - 1].Term != e.Term).ToList();

        if (newEntries.Any())
        {
            var firstNewIndex = newEntries.First().Index;

            // Rule 3: Truncate the log from the first conflicting entry.
            if (firstNewIndex <= State.Logs.Count)
            {
                var removedCount = State.Logs.Count - (firstNewIndex - 1);
                _logger.LogDebug("Node {NodeId} is truncating log from index {Index} ({Count} entries).", Id, firstNewIndex, removedCount);
                State.Logs.RemoveRange(firstNewIndex - 1, removedCount);
            }

            // Rule 4:Append new entries.
            _logger.LogDebug("Node {NodeId} is appending {Count} new entries.", Id, newEntries.Count);
            State.Logs.AddRange(newEntries);
        }
        else
        {
            _logger.LogTrace("Node {NodeId} has no new entries to append.", Id);
        }

        // Rule 5: If leaderCommit > commitIndex, set commitIndex = min(leaderCommit, index of last new entry).
        if (leaderCommit > State.CommitIndex)
        {
            // why lstEntryIndex: because what if logs haven't reached here yet, or this node came back after some time
            // and meanwhile leaders commit index has advanced, due to achieving majority from the rest of the nodes.
            var lastEntryIndex = State.Logs.Count;
            State.CommitIndex = Math.Min(leaderCommit, lastEntryIndex);
            _logger.LogDebug("Node {NodeId} advancing CommitIndex to {CommitIndex}", Id, State.CommitIndex);
            ApplyCommittedEntries();
        }

        response.Success = true;
        response.Term = State.CurrentTerm;
        response.WithAck();
        return response;
    }

    internal void HandleClientWrite(byte[] command, TaskCompletionSource<object?> tcs)
    {
        if (CurrentRole != Role.Leader)
        {
            // TODO: redirect to leader
            _logger.LogWarning("Node {NodeId} (not a leader) received a client write. Ignoring.", Id);
            tcs.SetException(new Exception("Not a leader"));
            return;
        }
        var log = new Log
        {
            Term = State.CurrentTerm,
            Index = State.Logs.Count + 1, // Use 1-based indexing
            Command = command
        };
        _logger.LogDebug("Node {NodeId} (Leader) received client write. Creating new log entry at Index {Index}, Term {Term}", Id, log.Index, log.Term);
        _pendingClients.Add(log.Index, tcs);
        State.Logs.Add(log);
        var followUps = new IRaftEvent[] { new LogPersistedEvent(log.Index) };
        statePersister.Enqueue(new PersistentCommand(State.GetPersistentState(), followUpEvents: followUps));
        _nodeEnvelope.SignalAllReplicators();

    }

    internal void HandleAppendResponse(
    int followerId,
    int term,
    bool success,
    int matchIndex)
    {
        if (term > State.CurrentTerm)
        {
            BecomeFollower(term);
            return;
        }

        if (CurrentRole != Role.Leader)
        {
            _logger.LogTrace("Node {NodeId} ignored AppendResponse from {FollowerId} because it's no longer the Leader.", Id, followerId);
            return;
        }

        if (!success)
        {
            // read line 126 
            State.NextIndex[followerId]--;
            _logger.LogDebug("Node {NodeId} received unsuccessful AppendResponse from {FollowerId}. Decrementing NextIndex to {NextIndex}", Id, followerId, State.NextIndex[followerId]);
            _nodeEnvelope.SignalReplicator(followerId);
            return;
        }

        State.MatchIndex[followerId] = matchIndex;
        State.NextIndex[followerId] = matchIndex + 1;
        _logger.LogTrace("Node {NodeId} received successful AppendResponse from {FollowerId}. Updated MatchIndex: {MatchIndex}, NextIndex: {NextIndex}", Id, followerId, State.MatchIndex[followerId], State.NextIndex[followerId]);

        TryAdvanceCommitIndex();
    }

    internal void MarkDurable(int index)
    {
        _durable.Add(index);
        TryAdvanceCommitIndex();
    }

    private void TryAdvanceCommitIndex()
    {
        // Find the highest log index 'i' that has been replicated on a majority of servers.
        for (int i = State.Logs.Count; i > State.CommitIndex; i--)
        {
            if (!_durable.Contains(i))
                continue;

            int replicated = 1; // leader
            foreach (var m in State.MatchIndex.Values)
                if (m >= i) replicated++;

            _logger.LogTrace("Node {NodeId} checking commit for index {Index}. Replicated on {ReplicatedCount} nodes.", Id, i, replicated);
            // This check is crucial for correctness, leader can only
            // commit entries from its own term by counting replicas. By committing a new
            // entry from its current term (like the no-op entry on election), it implicitly
            // commits all preceding entries. (fig 8, raft paper)
            if (replicated * 2 > _configuration.ClusterSize &&
                State.Logs[i - 1].Term == State.CurrentTerm)
            {
                _logger.LogDebug("Node {NodeId} advancing CommitIndex to {NewCommitIndex}", Id, i);
                State.CommitIndex = i;
                break; // Found the highest new commit index, no need to check lower ones.
            }
        }

        ApplyCommittedEntries();
    }

    private void ApplyCommittedEntries()
    {
        while (State.LastApplied < State.CommitIndex)
        {
            var entry = State.Logs[State.LastApplied];
            _logger.LogDebug("Node {NodeId} applying entry {Index} to state machine.", Id, entry.Index);
            var result = ApplyToStateMachine(entry.Command);
            State.LastApplied++;

            // Only the leader will have pending client requests to notify.
            if (CurrentRole == Role.Leader)
            {
                if (_pendingClients.TryGetValue(State.LastApplied, out var tcs))
                {
                    _logger.LogTrace("Node {NodeId} completing pending client request for log index {LogIndex}", Id, State.LastApplied);
                    tcs.SetResult(result);
                    _pendingClients.Remove(State.LastApplied);
                }
            }
        }
        _nodeEnvelope.AllEntriesCommitted();
    }

    private object? ApplyToStateMachine(byte[] command)
    {
        return _stateMachine.Apply(command);
    }

    internal ReplicationSnapshot GetReplicationSnapshot(int followerId)
    {
        var next = State.NextIndex[followerId];
        var prevIndex = next - 1;

        return new ReplicationSnapshot
        {
            Term = State.CurrentTerm,
            LeaderId = Id,
            PrevLogIndex = prevIndex,
            PrevLogTerm = prevIndex > 0 ? State.Logs[prevIndex - 1].Term : 0,
            Entries = State.Logs.Skip(prevIndex).ToList(),
            LeaderCommit = State.CommitIndex
        };
    }

    internal void HandleHeartBeat()
    {
        if (CurrentRole != Role.Leader)
        {
            _logger.LogWarning("Node {NodeId} (not a leader) received a heartbeat event. Ignoring.", Id);
            return;
        }
        _logger.LogDebug("Node {NodeId} (Leader) sending heartbeat.", Id);
        _nodeEnvelope.SignalAllReplicators();
    }

    internal void StartElection()
    {
        BecomeCandidate();
        // Persist state (new term and vote) BEFORE sending out vote requests.
        // After persistence, a SendVoteRequestsEvent will be enqueued.
        var followUps = new IRaftEvent[] { new SendVoteRequestsEvent(State.CurrentTerm) };
        statePersister.Enqueue(new PersistentCommand(
            State.GetPersistentState(),
            followUpEvents: followUps));
    }
    internal void SendVoteRequests()
    {
        _logger.LogDebug("Node {NodeId} (Candidate) sending vote requests for Term {Term}", Id, State.CurrentTerm);
        _nodeEnvelope.SignalAllVoteRequesters();
    }

    private void BecomeCandidate()
    {
        State.CurrentTerm++;
        State.VotedFor = Id;
        CurrentRole = Role.Candidate;
        _logger.LogInformation("Node {NodeId} became Candidate for Term {Term}", Id, State.CurrentTerm);
        _nodeEnvelope.ActivityTracker.LastActivityTicks = _nodeEnvelope.ActivityTracker.Now;
        _votesReceived = new HashSet<int> { Id };
    }

    private void BecomeLeader()
    {
        // A node can only transition to Leader from the Candidate state.
        // This is a safeguard against incorrect state transitions.
        if (CurrentRole != Role.Candidate)
        {
            _logger.LogWarning("Node {NodeId} cannot become leader; it is not a candidate. Current role: {Role}", Id, CurrentRole);
            return;
        }

        CurrentRole = Role.Leader;
        _votesReceived.Clear();
        _logger.LogInformation("Node {NodeId} became Leader for Term {Term}", Id, State.CurrentTerm);

        // Initialize leader volatile state
        State.NextIndex = new Dictionary<int, int>();
        State.MatchIndex = new Dictionary<int, int>();
        var lastLogIndex = State.Logs.Count;
        for (int i = 0; i < _configuration.ClusterSize; i++)
        {
            if (i == Id) continue;
            State.NextIndex[i] = lastLogIndex + 1;
            State.MatchIndex[i] = 0;
        }

        // a leader must commit an entry from its current term
        // to be able to commit entries from previous terms. We add a no-op entry to the log
        // upon election to ensure this happens promptly.
        var noOpLog = new Log
        {
            Term = State.CurrentTerm,
            Index = State.Logs.Count + 1,
            Command = Array.Empty<byte>() // A no-op entry has an empty command.
        };
        _logger.LogDebug("Node {NodeId} (Leader) adding no-op entry at Index {Index} for Term {Term}", Id, noOpLog.Index, State.CurrentTerm);
        State.Logs.Add(noOpLog);
        var followUps = new IRaftEvent[] { new LogPersistedEvent(noOpLog.Index) };
        statePersister.Enqueue(new PersistentCommand(State.GetPersistentState(), followUpEvents: followUps));

        // Now, send heartbeats which will also carry this new no-op entry.
        HandleHeartBeat();
    }

    internal RequestVoteSnapshot GetRequestVoteSnapshot()
    {
        return new RequestVoteSnapshot
        {
            Term = State.CurrentTerm,
            CandidateId = Id,
            LastLogIndex = State.Logs.Count, // 1-based index of last log
            LastLogTerm = State.Logs.LastOrDefault()?.Term ?? 0
        };
    }

    internal void HandleRequestVoteResponse(int peerId, int term, bool voteGranted)
    {
        if (term > State.CurrentTerm)
        {
            BecomeFollower(term);
            return; // We are a follower now, stop processing the vote.
        }

        if (CurrentRole != Role.Candidate || term < State.CurrentTerm)
        {
            _logger.LogTrace("Node {NodeId} ignored vote response from Peer {PeerId}. Current Role: {CurrentRole}, Term is stale: {IsStale}", Id, peerId, CurrentRole, term < State.CurrentTerm);
            return;
        }

        if (voteGranted)
        {
            _logger.LogDebug("Node {NodeId} received a vote from Peer {PeerId} in Term {Term}", Id, peerId, term);
            _votesReceived.Add(peerId);
            _logger.LogTrace("Node {NodeId} now has {_votesReceived.Count} votes in Term {Term}.", Id, _votesReceived.Count, State.CurrentTerm);
            int majority = (_configuration.ClusterSize / 2) + 1;
            if (_votesReceived.Count >= majority)
            {
                BecomeLeader();
            }
        }
    }

    private void BecomeFollower(int newTerm)
    {
        // Don't go backwards in term. This is a safeguard.
        if (newTerm < State.CurrentTerm)
        {
            _logger.LogWarning("Node {NodeId} received a call to BecomeFollower with a stale term {NewTerm}. Current term is {CurrentTerm}.", Id, newTerm, State.CurrentTerm);
            return;
        }

        var previousRole = CurrentRole;
        bool termChanged = newTerm > State.CurrentTerm;

        if (previousRole == Role.Candidate)
        {
            _votesReceived.Clear();
        }

        CurrentRole = Role.Follower;
        State.CurrentTerm = newTerm;

        if (termChanged)
        {
            _logger.LogInformation("Node {NodeId} became Follower. Previous Role: {PreviousRole}, New Term: {NewTerm}", Id, previousRole, newTerm);
        }
        else
        {
            _logger.LogTrace("Node {NodeId} is already a Follower in term {Term}. Role remains {Role}.", Id, newTerm, previousRole);
        }

        // Per Raft spec, VotedFor is reset when the term changes.
        if (termChanged)
        {
            State.VotedFor = null;
        }

        // Only persist state if the term changed.
        if (termChanged)
        {
            statePersister.Enqueue(new PersistentCommand(State.GetPersistentState()));
        }
    }
}