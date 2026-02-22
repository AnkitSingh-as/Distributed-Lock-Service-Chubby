using Microsoft.VisualStudio.TestTools.UnitTesting;
using Raft;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Raft.Distributed.Tests
{
    [TestClass]
    public class LeaderElectionTests
    {

        private async Task CheckNoLeaderIsElected(InProcessNetwork network)
        {
            network.Clock.Advance(TimeSpan.FromSeconds(2));
            await Task.Delay(50);
            var activeNodes = network.Nodes.Where(n => n.Node.State.CurrentTerm > 0 && network.IsConnected(n.Node.Id) && n.Node.CurrentRole == Role.Leader).ToList();
            Assert.AreEqual(0, activeNodes.Count, "No leader should have been elected.");
        }



        private async Task<NodeEnvelope> LeaderElection(InProcessNetwork network, IEnumerable<NodeEnvelope>? nodeSubset = null)
        {
            var nodes = nodeSubset ?? network.Nodes;
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            while (stopwatch.Elapsed < TimeSpan.FromSeconds(2))
            {
                network.Clock.Advance(TimeSpan.FromMilliseconds(150));
                await Task.Delay(50);
                var activeNodes = nodes.Where(n => n.Node.State.CurrentTerm > 0 && network.IsConnected(n.Node.Id)).ToList();
                if (!activeNodes.Any()) continue;

                var maxTerm = activeNodes.Max(n => n.Node.State.CurrentTerm);
                var leaders = activeNodes.Where(n => n.Node.CurrentRole == Role.Leader && n.Node.State.CurrentTerm == maxTerm).ToList();

                if (leaders.Count == 1)
                {
                    return leaders.Single();
                }
            }

            Assert.Fail("A single leader was not elected within the time limit.");
            return null;
        }

        [TestMethod]
        public async Task TestAtMostOneLeaderIsElected()
        {
            var network = new InProcessNetwork(3);
            await network.Start();

            var leader = await LeaderElection(network);
            Assert.IsNotNull(leader, "A leader should have been elected.");

            for (int i = 0; i < 10; i++)
            {
                network.Clock.Advance(TimeSpan.FromMilliseconds(50));
                await Task.Delay(10);
                var leaders = network.Nodes.Where(n => n.Node.CurrentRole == Role.Leader).ToList();
                Assert.AreEqual(1, leaders.Count, "There should still be only one leader.");
                Assert.AreEqual(leader.Node.Id, leaders.Single().Node.Id, "The leader should not have changed.");
            }
        }

        [TestMethod]
        public async Task NewLeaderIsElectedWhenLeaderDisconnects()
        {
            var network = new InProcessNetwork(3);
            try
            {
                await network.Start();

                var oldLeader = await LeaderElection(network);
                var oldLeaderId = oldLeader.Node.Id;
                var oldTerm = oldLeader.Node.State.CurrentTerm;

                network.DisconnectNode(oldLeaderId);

                var remainingNodes = network.Nodes.Where(n => n.Node.Id != oldLeaderId);
                var newLeader = await LeaderElection(network, remainingNodes);

                Assert.IsNotNull(newLeader, "A new leader should have been elected from the remaining nodes.");
                Assert.AreNotEqual(oldLeaderId, newLeader.Node.Id, "The new leader should be different from the old leader.");
                Assert.IsTrue(newLeader.Node.State.CurrentTerm > oldTerm, "The new leader should have a higher term.");
            }
            finally
            {
                network.Shutdown();
            }
        }

        [TestMethod]
        public async Task AfterNoQuorumLeaderIsElectedWhenNodeJoinsBack()
        {
            var network = new InProcessNetwork(3);
            await network.Start();
            var initialLeader = await LeaderElection(network);
            var initialLeaderId = initialLeader.Node.Id;
            var initialTerm = initialLeader.Node.State.CurrentTerm;
            network.DisconnectNode(initialLeaderId);
            var remainingNodes = network.Nodes.Where(n => n.Node.Id != initialLeaderId);
            var secondLeader = await LeaderElection(network, remainingNodes);
            Assert.IsNotNull(secondLeader, "A second leader should have been elected.");
            network.DisconnectNode(secondLeader.Node.Id);
            await CheckNoLeaderIsElected(network);
            network.ReconnectNode(secondLeader.Node.Id);
            var finalLeader = await LeaderElection(network);
            Assert.IsNotNull(finalLeader, "A final leader should have been elected after quorum was restored.");
            Assert.AreNotEqual(initialLeaderId, finalLeader.Node.Id, "The new leader should not be the same as the initial leader.");
            Assert.IsTrue(finalLeader.Node.State.CurrentTerm > initialTerm, "The new leader should have a higher term than the initial leader.");
        }


        [TestMethod]
        public async Task NewElectionOccursAfterFollowerReconnectsFromLongPartition()
        {
            var network = new InProcessNetwork(3);
            await network.Start();
            var originalLeader = await LeaderElection(network);
            var originalLeaderId = originalLeader.Node.Id;
            var originalTerm = originalLeader.Node.State.CurrentTerm;
            var followerToDisconnectId = network.Nodes.First(n => n.Node.Id != originalLeaderId).Node.Id;
            network.DisconnectNode(followerToDisconnectId);
            network.Clock.Advance(TimeSpan.FromMilliseconds(50));
            await Task.Delay(300);
            var connectedNodes = network.Nodes.Where(n => network.IsConnected(n.Node.Id));
            var currentLeader = await LeaderElection(network, connectedNodes);
            Assert.IsNotNull(currentLeader, "A leader should be present in the connected partition.");
            network.ReconnectNode(followerToDisconnectId);
            network.Clock.Advance(TimeSpan.FromMilliseconds(650));
            await Task.Delay(50);
            var finalLeader = await LeaderElection(network);
            Assert.IsTrue(finalLeader.Node.State.CurrentTerm > originalTerm, "A new election should occur, resulting in a higher term.");
        }

        [TestMethod]
        public async Task TestElectionLosesAndRegainsQuorumInLoop()
        {
            var network = new InProcessNetwork(3);
            await network.Start();

            for (int i = 0; i < 3; i++)
            {
                var leader = await LeaderElection(network);
                var leaderId = leader.Node.Id;
                var otherNodeId = network.Nodes.First(n => n.Node.Id != leaderId).Node.Id;
                network.DisconnectNode(leaderId);
                network.DisconnectNode(otherNodeId);
                await CheckNoLeaderIsElected(network);
                network.ReconnectNode(leaderId);
                network.ReconnectNode(otherNodeId);
            }
        }

    }
}
