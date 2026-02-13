namespace KafkaLens.Formatting;

public class NumberFormatter : IMessageFormatter
{
    public string Name => "Number";

    public string? Format(byte[] data, bool prettyPrint)
    {
        if (data == null || data.Length == 0)
        {
            return null;
        }

        try
        {
            byte[] clone = (byte[])data.Clone();
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(clone);
            }

            if (clone.Length == 4)
            {
                return BitConverter.ToInt32(clone, 0).ToString();
            }
            if (clone.Length == 8)
            {
                return BitConverter.ToInt64(clone, 0).ToString();
            }
            if (clone.Length == 2)
            {
                return BitConverter.ToInt16(clone, 0).ToString();
            }
        }
        catch
        {
            // ignore
        }
        return null;
    }

    public string? Format(byte[] data, string searchText, bool useObjectFilter = true)
    {
        var formatted = Format(data, true);
        if (formatted != null && (string.IsNullOrEmpty(searchText) || formatted.Contains(searchText, StringComparison.OrdinalIgnoreCase)))
        {
            return formatted;
        }
        return null;
    }
}
