using KafkaLens.Formatting;
using KafkaLens.Shared.Models;

namespace KafkaLens.ViewModels.Services;

public interface IFormatterService
{
    IMessageFormatter? GuessValueFormatter(Message message, IList<string> allowedValueFormatterNames);
    IMessageFormatter? GuessKeyFormatter(Message message, IList<string> allowedKeyFormatterNames);
    string NormalizeFormatterName(string? formatterName, IList<string> allowedNames);
    bool CanApplyFormatterToLoadedMessages(string? formatterName, IList<string> allowedNames);
    IList<string> BuildFormatterNames(string? configuredRaw, IList<string> allowed);
    bool IsUnknownFormatter(string? formatterName);
    string? ToKnownFormatterOrNull(string? formatterName);
    string GetDefaultFormatterName();
    IList<string> GetAllFormatterNames();
    IList<string> GetBuiltInKeyFormatterNames();
}
