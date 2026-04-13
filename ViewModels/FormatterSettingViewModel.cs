using CommunityToolkit.Mvvm.ComponentModel;

namespace KafkaLens.ViewModels;

public partial class FormatterSettingViewModel : ObservableObject
{
    public string Name { get; }

    [ObservableProperty]
    private bool isEnabledForValues;

    [ObservableProperty]
    private bool isEnabledForKeys;

    public FormatterSettingViewModel(string name, bool isEnabledForValues, bool isEnabledForKeys)
    {
        Name = name;
        IsEnabledForValues = isEnabledForValues;
        IsEnabledForKeys = isEnabledForKeys;
    }
}
