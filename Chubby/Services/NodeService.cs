using Grpc.Core;
using Raft;
using Raft.Protos;

namespace Chubby.Services;

public class RaftService : RaftServer.RaftServerBase
{
    private readonly IServer server;

    public RaftService(IServer nodeEnvelope)
    {
        this.server = nodeEnvelope;
    }

    public override async Task<AppendEntriesResponse> AppendEntries(AppendEntriesRequest request, ServerCallContext context)
    {
        var domainLogs = request.Entries.Select(e => new Log
        {
            Term = e.Term,
            Command = e.Command.ToByteArray(),
            Index = e.Index
        }).ToList();

        var response = await server.AppendEntries(request.Term, request.LeaderId, request.PrevLogIndex, request.PrevLogTerm, domainLogs, request.LeaderCommit);

        return new AppendEntriesResponse
        {
            Term = response.Term,
            Success = response.Success
        };
    }

    public override async Task<Raft.Protos.RequestVoteResponse> RequestVote(RequestVoteRequest request, ServerCallContext context)
    {
        if (!int.TryParse(request.CandidateId, out var candidateId))
        {
            throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, $"Invalid CandidateId format: {request.CandidateId}"));
        }

        var response = await server.RequestVote(request.Term, candidateId, request.LastLogIndex, request.LastLogTerm);

        return new Raft.Protos.RequestVoteResponse
        {
            Term = response.Term,
            VoteGranted = response.VoteGranted
        };
    }
}