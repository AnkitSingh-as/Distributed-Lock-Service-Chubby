using Raft;

public interface IServer
{
    public Task<AppendEntryResponse> AppendEntries(int term, int leaderId, int prevLogIndex, int prevLogTerm, List<Log> entries, int leaderCommit);
    public Task<RequestVoteResponse> RequestVote(int term, int candidateId, int lastLogIndex, int lastLogTerm);
}