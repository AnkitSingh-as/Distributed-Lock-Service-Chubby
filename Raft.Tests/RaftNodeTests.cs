using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Raft.Tests.Fakes;

namespace Raft.Tests
{
    [TestClass]
    public class RaftNodeTests
    {
        private FakeRaftClock _clock;
        private FakeNodeEnvelope _nodeEnvelope;
        private RaftNode _node;
        private RaftConfig _config;
        private State _state;

        [TestInitialize]
        public void TestInitialize()
        {
            _config = new RaftConfig { ClusterSize = 3 };
            _state = new State();
            _clock = new FakeRaftClock();
            _nodeEnvelope = new FakeNodeEnvelope(_clock);
            var logger = new NullLogger<RaftNode>();
            var fakeStateMachine = new FakeStateMachine();
            _node = new RaftNode(_config, _state, _nodeEnvelope, 0, fakeStateMachine, logger);
        }

        [TestMethod]
        public void InitialState_IsFollower()
        {

            Assert.AreEqual(Role.Follower, _node.CurrentRole);
            Assert.AreEqual(0, _node.State.CurrentTerm);
        }

        [TestMethod]
        public void Follower_OnElectionTimeout_BecomesCandidate()
        {

            var electionTimeout = new ElectionTimeoutEvent();
            electionTimeout.Apply(_node);
            Assert.AreEqual(Role.Candidate, _node.CurrentRole);
            Assert.AreEqual(1, _node.State.CurrentTerm);
            Assert.AreEqual(0, _node.State.VotedFor);

            var persister = (FakeStatePersister)_nodeEnvelope.StatePersister;
            Assert.AreEqual(1, persister.EnqueuedCommands.Count);
            var command = persister.EnqueuedCommands[0];
            Assert.AreEqual(1, command.PersistentState.CurrentTerm);
            Assert.AreEqual(0, command.PersistentState.VotedFor);
            Assert.AreEqual(1, command.FollowUpEvents.Count);
            Assert.IsInstanceOfType(command.FollowUpEvents[0], typeof(SendVoteRequestsEvent));
        }

        [TestMethod]
        public void Candidate_OnSendVoteRequests_SignalsVoteRequesters()
        {
            new ElectionTimeoutEvent().Apply(_node);
            var sendVoteRequests = new SendVoteRequestsEvent(1);

            sendVoteRequests.Apply(_node);
            Assert.AreEqual(1, _nodeEnvelope.SignalAllVoteRequestersCalls);
        }

        [TestMethod]
        public void Candidate_ReceivesEnoughVotes_BecomesLeader()
        {
            new ElectionTimeoutEvent().Apply(_node);
            _node.HandleRequestVoteResponse(1, 1, true);
            Assert.AreEqual(Role.Leader, _node.CurrentRole);
        }

        [TestMethod]
        public void Follower_GrantsVote_WhenLogIsUpToDate()
        {
            new RequestVoteEvent(1, 1, 0, 0).Apply(_node);

            Assert.AreEqual(1, _node.State.CurrentTerm);
            Assert.AreEqual(1, _node.State.VotedFor);

            var persister = (FakeStatePersister)_nodeEnvelope.StatePersister;
            Assert.AreEqual(2, persister.EnqueuedCommands.Count);
            var command = persister.EnqueuedCommands[1];
            Assert.AreEqual(1, command.PersistentState.CurrentTerm);
            Assert.AreEqual(1, command.PersistentState.VotedFor);
        }

        [TestMethod]
        public void Follower_DeniesVote_WhenLogIsLessUpToDate()
        {

            _node.State.CurrentTerm = 1;
            _node.State.Logs.Add(new Log { Term = 1, Index = 1, Command = [] });

            var response = _node.HandleRequestVote(2, 2, 0, 0);
            Assert.IsFalse(response.VoteGranted);
            Assert.AreEqual(2, response.Term);
            Assert.AreEqual(2, _node.State.CurrentTerm);
            Assert.IsNull(_node.State.VotedFor);

            var persister = (FakeStatePersister)_nodeEnvelope.StatePersister;
            Assert.AreEqual(1, persister.EnqueuedCommands.Count);
            var command = persister.EnqueuedCommands[0];
            Assert.AreEqual(2, command.PersistentState.CurrentTerm);
            Assert.IsNull(command.PersistentState.VotedFor);
        }


        public void CandidateWhenReceivesAppendEntries_WithHigherTerm_StepsDown()
        {
            new ElectionTimeoutEvent().Apply(_node);
            new AppendEntriesEvent(2, 1, 0, 0, new List<Log>(), 0).Apply(_node);
            Assert.AreEqual(Role.Follower, _node.CurrentRole);
            Assert.AreEqual(2, _node.State.CurrentTerm);
        }

        [TestMethod]
        public void FollowerWhenReceivesAppendEntries_AndTermIsLower_Rejects()
        {
            _node.CurrentRole = Role.Follower;
            _node.State.CurrentTerm = 3;
            _node.State.VotedFor = 1;
            var response = _node.HandleAppendEntries(2, 1, 0, 0, new List<Log>(), 0);
            Assert.IsFalse(response.Success);
            Assert.AreEqual(3, response.Term);
            Assert.AreEqual(3, _node.State.CurrentTerm);
        }

        [TestMethod]
        public void FollowerWhenReceivesAppendEntries_AndLogIsInconsistent_Rejects()
        {
            var response = _node.HandleAppendEntries(1, 1, 1, 1, new List<Log>(), 0);
            Assert.IsFalse(response.Success);
            Assert.AreEqual(1, response.Term);
            Assert.AreEqual(1, _node.State.CurrentTerm);
        }

        [TestMethod]
        public void FollowerWhenDoesNotGrantVote_IfAlreadyVoted()
        {
            new RequestVoteEvent(1, 1, 0, 0).Apply(_node);
            Assert.AreEqual(1, _node.State.VotedFor);
            new RequestVoteEvent(1, 2, 0, 0).Apply(_node);
            Assert.AreEqual(1, _node.State.VotedFor);
        }

        [TestMethod]
        public void CandidateWhenReceivesRequestVote_WithHigherTerm_GrantsVoteAndStepsDown()
        {
            new ElectionTimeoutEvent().Apply(_node);
            new RequestVoteEvent(2, 1, 0, 0).Apply(_node);
            Assert.AreEqual(Role.Follower, _node.CurrentRole);
            Assert.AreEqual(2, _node.State.CurrentTerm);
            Assert.AreEqual(1, _node.State.VotedFor);
        }

        [TestMethod]
        public void CandidateWhenElectionTimesOut_StartsNewElection()
        {
            new ElectionTimeoutEvent().Apply(_node);
            Assert.AreEqual(Role.Candidate, _node.CurrentRole);
            Assert.AreEqual(1, _node.State.CurrentTerm);
            new ElectionTimeoutEvent().Apply(_node);
            Assert.AreEqual(Role.Candidate, _node.CurrentRole);
            Assert.AreEqual(2, _node.State.CurrentTerm);
            Assert.AreEqual(0, _node.State.VotedFor);

            var persister = (FakeStatePersister)_nodeEnvelope.StatePersister;
            Assert.AreEqual(2, persister.EnqueuedCommands.Count);

            var command2 = persister.EnqueuedCommands[1];
            Assert.AreEqual(2, command2.PersistentState.CurrentTerm);
            Assert.AreEqual(0, command2.PersistentState.VotedFor);
            Assert.AreEqual(1, command2.FollowUpEvents.Count);
            Assert.IsInstanceOfType(command2.FollowUpEvents[0], typeof(SendVoteRequestsEvent));
        }

        [TestMethod]
        public void LeaderWhenAppointed_SendsHeartbeatsToFollowers()
        {
            _node.State.CurrentTerm = 1;
            _node.CurrentRole = Role.Leader;
            _node.State.VotedFor = 0;
            Assert.AreEqual(Role.Leader, _node.CurrentRole);
            new HeartBeatEvent().Apply(_node);
            Assert.AreEqual(1, _nodeEnvelope.SignalAllReplicatorsCalls);
        }

        [TestMethod]
        public void LeaderWhenReceivesWriteCommand_AppendsToOwnLogAndSendsToFollowers()
        {
            _node.State.CurrentTerm = 1;
            _node.CurrentRole = Role.Leader;
            _node.State.VotedFor = 0;
            new WriteEvent([], RaftTcs.Create<object?>()).Apply(_node);
            Assert.AreEqual(1, _node.State.Logs.Count);
            Assert.AreEqual([], _node.State.Logs[0].Command);
            Assert.AreEqual(1, _nodeEnvelope.SignalAllReplicatorsCalls);
        }


        [TestMethod]
        public void LeaderWhenMajorityOfLogsAreReplicated_UpdatesCommitIndex()
        {
            _node.State.CurrentTerm = 1;
            _node.CurrentRole = Role.Leader;
            _node.State.VotedFor = 0;
            new WriteEvent([], RaftTcs.Create<object?>()).Apply(_node);
            new LogPersistedEvent(1).Apply(_node);
            new AppendEntriesResponseEvent(1, 1, true, 1).Apply(_node);
            Assert.AreEqual(1, _node.State.CommitIndex);

        }
    }
}