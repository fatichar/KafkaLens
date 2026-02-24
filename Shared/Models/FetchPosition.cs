namespace KafkaLens.Shared.Models;

public enum PositionType
{
    Timestamp,
    Offset
}
public class FetchPosition
{
    public static FetchPosition Start => new(PositionType.Offset, 0);
    public static FetchPosition End => new(PositionType.Offset, -1);
    public PositionType Type { get; private set; }
    public long Offset { get; private set; }
    public long Timestamp { get; private set; }
    public FetchPosition(PositionType type, long value)
    {
        Type = type;
        switch (type)
        {
            case PositionType.Offset:
                Offset = value;
                break;
            case PositionType.Timestamp:
                Timestamp = value;
                break;
        }
    }

    public void SetOffset(long offset)
    {
        Type = PositionType.Offset;
        Offset = offset;
    }
}