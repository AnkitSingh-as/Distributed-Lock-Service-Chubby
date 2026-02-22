namespace Chubby.Core.StateMachine;

public class ChubbyConfig
{
    public int LeaseTimeout { get; set; } = 12 * 1000;
    public int SessionExpiryTimeout { get; set; } = 1 * 60 * 1000;
    public int Threshold { get; set; } = 1 * 1000;
    public int EphemeralNodeCleanupTimeout { get; set; } = 1 * 60 * 1000;
}
