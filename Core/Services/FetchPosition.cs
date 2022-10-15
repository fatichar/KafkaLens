namespace KafkaLens.Core.Services;

public enum PositionType
{
    TIMESTAMP,
    OFFSET
}
public class FetchPosition
{
    public static FetchPosition START => new(PositionType.OFFSET, 0);
    public static FetchPosition END => new(PositionType.OFFSET, -1);
    public PositionType Type { get; private set; }
    public long Offset { get; private set; }
    public long Timestamp { get; private set; }
    public FetchPosition(PositionType type, long value)
    {
        Type = type;
        switch (type)
        {
            case PositionType.OFFSET:
                Offset = value;
                break;
            case PositionType.TIMESTAMP:
                Timestamp = value;
                break;
        }
    }

    internal void SetOffset(long offset)
    {
        Type = PositionType.OFFSET;
        Offset = offset;
    }
}