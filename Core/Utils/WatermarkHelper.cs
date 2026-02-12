using Confluent.Kafka;
using KafkaLens.Shared.Models;

namespace KafkaLens.Core.Utils;

public static class WatermarkHelper
{
    public static void UpdateForWatermarks(FetchOptions fetchOptions, WatermarkOffsets watermarks)
    {
        var position = fetchOptions.Start;
        var offset = position.Offset;
        if (offset < 0)
        {
            // if options.Start.Offset = -1 => offset = watermarks.High
            // means no message will be returned
            offset = watermarks.High.Value + 1 + offset;
        }

        if (offset < watermarks.Low.Value)
        {
            offset = watermarks.Low.Value;
        }
        else if (offset > watermarks.High.Value)
        {
            offset = watermarks.High.Value;
        }

        position.SetOffset(offset);
        if (position.Offset + fetchOptions.Limit > watermarks.High.Value)
        {
            fetchOptions.Limit = (int)(watermarks.High.Value - position.Offset);
        }
    }
}
