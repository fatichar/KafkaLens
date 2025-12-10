namespace KafkaLens.ViewModels;

public class MessageViewOptions
{
    public string FormatterName { get; set; } = "";
    public bool UseObjectFilter { get; set; }
    public string FilterText { get; set; } = "";
}