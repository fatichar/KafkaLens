# KafkaLens.Formatting

Message formatters and formatting interfaces for KafkaLens plugins.

## What's Included

- `IMessageFormatter` interface for creating custom message formatters
- Base formatter implementations
- Built-in formatters (JSON, Text, Numeric, Protobuf)

## Usage

### Create a Custom Formatter

```csharp
using KafkaLens.Formatting;

[KafkaLensExtension(typeof(IMessageFormatter))]
public class MyCustomFormatter : IMessageFormatter
{
    public string Name => "My Custom Formatter";
    
    public string? Format(byte[] data, bool prettyPrint)
    {
        var text = Encoding.UTF8.GetString(data);
        return ProcessText(text, prettyPrint);
    }
    
    public string? Format(byte[] data, string searchText, bool useObjectFilter = true)
    {
        var formatted = Format(data, true);
        if (formatted != null && !string.IsNullOrEmpty(searchText))
        {
            return HighlightSearchTerms(formatted, searchText);
        }
        return formatted;
    }
}
```

### Installation

Add to your plugin project:

```xml
<PackageReference Include="KafkaLens.Formatting" Version="0.8.4" />
```

## Built-in Formatters

- **JsonFormatter** - Formats JSON messages with pretty-printing
- **TextFormatter** - Plain text formatting
- **NumericFormatters** - Various numeric format conversions
- **ProtobufFormatter** - Protocol Buffers formatting

## Plugin Development

For complete plugin development guide, see:
[https://github.com/fatichar/KafkaLens/blob/main/docs/plugin-development.md](https://github.com/fatichar/KafkaLens/blob/main/docs/plugin-development.md)

## Requirements

- .NET 10.0
- Compatible with KafkaLens 0.8+

## License

AGPL-3.0-or-later - see [LICENSE](https://github.com/fatichar/KafkaLens/blob/main/LICENSE) for details.
