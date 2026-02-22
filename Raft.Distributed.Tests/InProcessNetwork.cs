using Microsoft.Extensions.Logging;
using Serilog;
using Raft.Tests.Fakes;
using Chubby.DataSource;
using System.Linq;
using System.Threading.Tasks;


namespace Raft.Distributed.Tests
{
    public class InProcessNetwork
    {
        private readonly Dictionary<int, NodeEnvelope> _nodes = new Dictionary<int, NodeEnvelope>();
        private readonly ILoggerFactory _loggerFactory;
        private readonly RaftConfig _config;
        private readonly FakeRaftClock _clock = new FakeRaftClock();
        private readonly HashSet<int> _disconnectedNodeIds = new HashSet<int>();

        public InProcessNetwork(int clusterSize)
        {
            _config = new RaftConfig
            {
                ClusterSize = clusterSize,
                ElectionTimeoutMinMs = 150,
                ElectionTimeoutMaxMs = 300,
                HeartbeatIntervalMs = 50
            };

            var serilogLogger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console()
                .CreateLogger();

            _loggerFactory = new LoggerFactory().AddSerilog(serilogLogger);
        }

        public async Task Start()
        {
            for (int i = 0; i < _config.ClusterSize; i++)
            {
                var dataSource = new InMemoryDataSource();
                var stateMachine = new FakeStateMachine();
                var node = await NodeEnvelope.CreateAsync(dataSource, _config, i, stateMachine, _clock, _loggerFactory);
                _nodes.Add(i, node);
            }

            var allPeers = _nodes.ToDictionary(
                kvp => kvp.Key,
                kvp => (IServer)new InProcessServer(kvp.Value, _loggerFactory.CreateLogger<InProcessServer>()));

            foreach (var node in _nodes.Values)
            {
                node.Peers = new Dictionary<int, IServer>(allPeers);
            }
        }

        public IEnumerable<NodeEnvelope> Nodes => _nodes.Values;
        public FakeRaftClock Clock => _clock;

        private class InProcessServer : IServer
        {
            private readonly NodeEnvelope _node;
            private readonly ILogger<InProcessServer> _logger;

            public InProcessServer(NodeEnvelope node, ILogger<InProcessServer> logger)
            {
                _node = node;
                _logger = logger;
            }

            public async Task<AppendEntryResponse> AppendEntries(int term, int leaderId, int prevLogIndex, int prevLogTerm, List<Log> entries, int leaderCommit)
            {
                _logger.LogTrace("In-process AppendEntries call to node {NodeId}", _node.Node.Id);
                return await _node.AppendEntries(term, leaderId, prevLogIndex, prevLogTerm, entries, leaderCommit);
            }

            public async Task<RequestVoteResponse> RequestVote(int term, int candidateId, int lastLogIndex, int lastLogTerm)
            {
                _logger.LogTrace("In-process RequestVote call to node {NodeId}", _node.Node.Id);
                return await _node.RequestVote(term, candidateId, lastLogIndex, lastLogTerm);
            }
        }

        private class DisconnectedInProcessServer : IServer
        {
            private readonly NodeEnvelope _node;
            private readonly ILogger<DisconnectedInProcessServer> _logger;

            public DisconnectedInProcessServer(NodeEnvelope node, ILogger<DisconnectedInProcessServer> logger)
            {
                _node = node;
                _logger = logger;
            }

            public Task<AppendEntryResponse> AppendEntries(int term, int leaderId, int prevLogIndex, int prevLogTerm, List<Log> entries, int leaderCommit)
            {
                _logger.LogTrace("In-process AppendEntries call to node {NodeId}", _node.Node.Id);
                return Task.FromException<AppendEntryResponse>(new Exception("Network Disconnected"));
            }

            public Task<RequestVoteResponse> RequestVote(int term, int candidateId, int lastLogIndex, int lastLogTerm)
            {
                _logger.LogTrace("In-process RequestVote call to node {NodeId}", _node.Node.Id);
                return Task.FromException<RequestVoteResponse>(new Exception("Network Disconnected"));
            }
        }


        public void DisconnectNode(int id)
        {
            if (!_nodes.ContainsKey(id)) return;

            _disconnectedNodeIds.Add(id);

            var disconnectedLogger = _loggerFactory.CreateLogger<DisconnectedInProcessServer>();
            var disconnectedNode = _nodes[id];

            foreach (var otherNode in _nodes.Values.Where(n => n.Node.Id != id))
            {
                otherNode.Peers[id] = new DisconnectedInProcessServer(disconnectedNode, disconnectedLogger);
                disconnectedNode.Peers[otherNode.Node.Id] = new DisconnectedInProcessServer(otherNode, disconnectedLogger);
            }
        }

        public void ReconnectNode(int id)
        {
            if (!_nodes.ContainsKey(id) || !_disconnectedNodeIds.Remove(id)) return;

            var serverLogger = _loggerFactory.CreateLogger<InProcessServer>();
            var disconnectedLogger = _loggerFactory.CreateLogger<DisconnectedInProcessServer>();
            var reconnectedNode = _nodes[id];

            reconnectedNode.Peers[id] = new InProcessServer(reconnectedNode, serverLogger);

            foreach (var otherNode in _nodes.Values.Where(n => n.Node.Id != id))
            {
                if (IsConnected(otherNode.Node.Id))
                {
                    reconnectedNode.Peers[otherNode.Node.Id] = new InProcessServer(otherNode, serverLogger);
                    otherNode.Peers[id] = new InProcessServer(reconnectedNode, serverLogger);
                }
                else
                {
                    reconnectedNode.Peers[otherNode.Node.Id] = new DisconnectedInProcessServer(otherNode, disconnectedLogger);
                }
            }
        }

        public bool IsConnected(int id)
        {
            return !_disconnectedNodeIds.Contains(id);
        }

        public void Shutdown()
        {
            foreach (var node in _nodes.Values)
            {
                node.Shutdown();
            }
            _loggerFactory.Dispose();
        }

    }

}
