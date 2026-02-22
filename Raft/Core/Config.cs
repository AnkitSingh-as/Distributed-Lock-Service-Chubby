namespace Raft;

public class RaftConfig
{
    public int ElectionTimeoutMinMs { get; set; } = 150;
    public int ElectionTimeoutMaxMs { get; set; } = 300;
    public int HeartbeatIntervalMs { get; set; } = 50;
    public int ClusterSize { get; set; } = 5;
    public Dictionary<int, string> Peers { get; set; } = new Dictionary<int, string>();
}
