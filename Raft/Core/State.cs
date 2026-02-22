namespace Raft;

public class State
{

    // Persistent state on all servers
    /// <summary>
    /// Latest term server has seen (initialized to 0 on first boot, increases monotonically)
    /// </summary>
    public int CurrentTerm { get; set; }

    /// <summary>
    /// CandidateId that received vote in current term (or null if none)    
    /// </summary>
    public int? VotedFor { get; set; }

    /// <summary>
    /// Log entries; each entry contains command for state machine, and term when entry was received by leader (first index is 1)
    /// </summary>
    public List<Log> Logs { get; set; } = new List<Log>();

    // Volatile state on all servers

    /// <summary>
    /// Index of highest log entry known to be committed (initialized to 0, increases monotonically)
    /// </summary>  
    /// <remarks>
    /// Initialized to 0 assuming log index starts at 1
    /// </remarks>
    public int CommitIndex { get; set; } = 0;

    /// <summary>
    /// Index of highest log entry applied to state machine (initialized to 0, increases monotonically)
    /// </summary>     
    /// <remarks>
    /// Initialized to 0 assuming log index starts at 1
    /// </remarks>
    /// </summary>
    public int LastApplied { get; set; } = 0;

    // Volatile state on leaders, reinitialized after election
    /// <summary>  
    /// For each server, index of the next log entry to send to that server (initialized to leader last log index + 1)
    /// </summary>
    public Dictionary<int, int> NextIndex { get; set; } = new Dictionary<int, int>();
    /// <summary>
    /// For each server, index of highest log entry known to be replicated on server (initialized to 0, increases monotonically)
    /// </summary>  
    public Dictionary<int, int> MatchIndex { get; set; } = new Dictionary<int, int>();

    public PersistentState  GetPersistentState()
    {
        return new PersistentState
        {
            CurrentTerm = this.CurrentTerm,
            VotedFor = this.VotedFor,
            Logs = this.Logs
        };
    }

}
    

public class PersistentState
{
    public int CurrentTerm { get; set; }
    public int? VotedFor { get; set; }
    public List<Log> Logs { get; set; } = new List<Log>();
}   