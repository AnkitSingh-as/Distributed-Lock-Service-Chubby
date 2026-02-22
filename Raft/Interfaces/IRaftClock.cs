public interface IRaftClock
{
    public long Now { get; }    
    public void Advance(TimeSpan timeSpan);
}
