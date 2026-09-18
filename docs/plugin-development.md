# KafkaLens Plugin Development

KafkaLens supports plugins for extending message formatting capabilities. This guide explains how to create and publish plugins.

## Prerequisites

Plugin developers need to reference two NuGet packages:

```xml
<PackageReference Include="KafkaLens.Shared" Version="1.0.*" />
<PackageReference Include="KafkaLens.Formatting" Version="1.0.*" />
```

Match the package version to the KafkaLens major version you're targeting.

## Plugin Types

### Message Formatters

Message formatters parse and display Kafka message data in various formats.

#### Create a Message Formatter

```csharp
using KafkaLens.Formatting;

[KafkaLensExtension(typeof(IMessageFormatter))]
public class MyCustomFormatter : IMessageFormatter
{
    public string Name => "My Custom Formatter";
    
    public string? Format(byte[] data, bool prettyPrint)
    {
        // Basic formatting implementation
        var text = Encoding.UTF8.GetString(data);
        return ProcessText(text, prettyPrint);
    }
    
    public string? Format(byte[] data, string searchText, bool useObjectFilter = true)
    {
        // Formatting with search highlighting
        var formatted = Format(data, true);
        if (formatted != null && !string.IsNullOrEmpty(searchText))
        {
            return HighlightSearchTerms(formatted, searchText);
        }
        return formatted;
    }
    
    private string ProcessText(string text, bool prettyPrint)
    {
        // Your custom formatting logic here
        return prettyPrint ? PrettyPrint(text) : text;
    }
    
    private string HighlightSearchTerms(string text, string searchTerm)
    {
        // Implement search highlighting logic
        return text.Replace(searchTerm, $"**{searchTerm}**");
    }
    
    private string PrettyPrint(string text)
    {
        // Implement pretty-printing logic
        return text; // Placeholder
    }
}
```

### Plugin Lifecycle

For plugins that need initialization or cleanup, implement `IKafkaLensPlugin`:

```csharp
using KafkaLens.Shared.Plugins;

public class MyPlugin : IKafkaLensPlugin
{
    public void Initialize(IServiceProvider services)
    {
        // Initialize plugin resources
        // Resolve services from the DI container if needed
    }
    
    public void Shutdown()
    {
        // Clean up resources
    }
}
```

## Building and Packaging

1. **Create a Class Library project**:
   ```bash
   dotnet new classlib -n MyKafkaLensPlugin
   cd MyKafkaLensPlugin
   ```

2. **Add package references**:
   ```xml
   <PackageReference Include="KafkaLens.Shared" Version="1.0.*" />
   <PackageReference Include="KafkaLens.Formatting" Version="1.0.*" />
   ```

3. **Implement your formatter(s)** as shown above

4. **Build the project**:
   ```bash
   dotnet build -c Release
   ```

5. **Package for distribution**:
   ```bash
   dotnet pack -c Release
   ```

## Installation

Users can install plugins through the KafkaLens Plugin Manager by:

1. Adding a repository URL that hosts your plugin's `.nupkg` file
2. Browsing available plugins
3. Clicking "Install" on your plugin

## Plugin Repository

To make your plugin discoverable, host a `plugins.json` file:

```json
{
  "name": "My Plugin Repository",
  "plugins": [
    {
      "id": "MyKafkaLensPlugin",
      "name": "My Custom Formatter",
      "description": "Custom message formatter for specific data format",
      "author": "Your Name",
      "version": "1.0.0",
      "kafkalensVersion": "1.0",
      "downloadUrl": "https://your-cdn.com/MyKafkaLensPlugin.1.0.0.nupkg",
      "sha256": "<sha256 of the .nupkg, optional>",
      "homepage": "https://github.com/yourusername/your-plugin"
    }
  ]
}
```

## Plugin Identity

Declare plugin metadata at assembly level:

```csharp
[assembly: KafkaLensPlugin(
    Id = "MyKafkaLensPlugin",
    Name = "CSV Formatter",
    Version = "1.0.0",
    Author = "Your Name",
    Description = "Displays CSV payloads with column-aware formatting")]
```

An optional `plugin.json` file inside the plugin folder can supplement or override the
assembly attribute with `id`, `name`, `version`, `author`, `description`, `homepage`,
`kafkalensVersion`, and `category` fields.

## Best Practices

1. **Version Compatibility**: Ensure your plugin targets the same .NET version (net10.0)
2. **Error Handling**: Implement robust error handling for malformed data
3. **Performance**: Consider performance for large messages
4. **Testing**: Include unit tests for your formatting logic
5. **Documentation**: Provide clear documentation for supported formats

## Examples

See the existing formatters in the KafkaLens repository:
- `JsonFormatter` - JSON message formatting
- `TextFormatter` - Plain text formatting
- `NumericFormatters` - Int8–UInt64 number formatting

## Support

For questions about plugin development:
- Check the KafkaLens repository issues
- Review existing plugin implementations
- Test thoroughly with various message types and sizes
