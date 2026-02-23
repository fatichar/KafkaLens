namespace KafkaLens.Shared.Models;

public enum FetchDirection
{
    Forward,
    Backward
}

public class FetchOptions
{
    public FetchOptions(FetchPosition start, int limit)
    {
        Start = start;
        Limit = limit;
    }
        
    public FetchOptions(FetchPosition start, FetchPosition? end)
    {
        Start = start;
        End = end;
    }

    public FetchPosition Start { get; set; } = FetchPosition.END;
    public FetchPosition? End { get; private set; }
    public int Limit { get; set; } = 10;
    public FetchDirection Direction { get; set; } = FetchDirection.Forward;

    public override string ToString() => $"Start = {Start}, End = {End}, Limit = {Limit}, Direction = {Direction}";
}