using Google.Protobuf;
using Raft;
using Raft.Protos;

namespace Chubby;

/// <summary>
/// An implementation of the IServer interface that acts as a gRPC client.
/// It translates domain models (like Raft.Log) to Protobuf models for outgoing requests
/// and converts Protobuf responses back into domain models.
/// This class is used by a Raft node to communicate with its peers.
/// </summary>
public class NodeClientImpl : IServer
{
    private readonly RaftServer.RaftServerClient _grpcClient;

    public NodeClientImpl(RaftServer.RaftServerClient grpcClient)
    {
        _grpcClient = grpcClient;
    }

    public async Task<AppendEntryResponse> AppendEntries(int term, int leaderId, int prevLogIndex, int prevLogTerm, List<Log> entries, int leaderCommit)
    {
        var request = new AppendEntriesRequest
        {
            Term = term,
            LeaderId = leaderId,
            PrevLogIndex = prevLogIndex,
            PrevLogTerm = prevLogTerm,
            LeaderCommit = leaderCommit
        };

        foreach (var entry in entries)
        {
            request.Entries.Add(new LogEntry
            {
                Term = entry.Term,
                Command = ByteString.CopyFrom(entry.Command),
                Index = entry.Index
            });
        }

        var response = await _grpcClient.AppendEntriesAsync(request);
        return new AppendEntryResponse
        {
            Term = response.Term,
            Success = response.Success
        };
    }

    public async Task<Raft.RequestVoteResponse> RequestVote(int term, int candidateId, int lastLogIndex, int lastLogTerm)
    {
        var request = new RequestVoteRequest
        {
            Term = term,
            CandidateId = candidateId.ToString(),
            LastLogIndex = lastLogIndex,
            LastLogTerm = lastLogTerm
        };

        var response = await _grpcClient.RequestVoteAsync(request);
        return new Raft.RequestVoteResponse
        {
            Term = response.Term,
            VoteGranted = response.VoteGranted
        };
    }
}