using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PricePin.Core.Market;
using PricePin.Services;

namespace PricePin.ViewModels;

/// <summary>One entry in the "Data source" menu. Text is looked up so it follows the UI language.</summary>
public sealed partial class DataSourceOption : ObservableObject
{
    private readonly string? _labelKey;
    private readonly string _name;
    private readonly string _descriptionKey;

    /// <param name="name">Exchange name, used as the label when <paramref name="labelKey"/> is null.</param>
    public DataSourceOption(DataSource value, string name, string? labelKey, string descriptionKey, Action<DataSourceOption> select)
    {
        Value = value;
        _name = name;
        _labelKey = labelKey;
        _descriptionKey = descriptionKey;
        SelectCommand = new RelayCommand(() => select(this));
    }

    public DataSource Value { get; }
    public string Label => _labelKey is null ? _name : Loc.T(_labelKey);
    public string Description => Loc.T(_descriptionKey);
    public ICommand SelectCommand { get; }

    [ObservableProperty]
    private bool _isSelected;

    public void RefreshText()
    {
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(Description));
    }
}

/// <summary>One language in the language picker.</summary>
public sealed partial class LanguageOption : ObservableObject
{
    public LanguageOption(string code, string? label, Action<LanguageOption> select)
    {
        Code = code;
        _fixedLabel = label;
        SelectCommand = new RelayCommand(() => select(this));
    }

    private readonly string? _fixedLabel;

    public string Code { get; }

    /// <summary>Native name ("Türkçe"); the "same as Windows" entry is translated.</summary>
    public string Label => _fixedLabel ?? Loc.T("General.LanguageSystem");

    public ICommand SelectCommand { get; }

    [ObservableProperty]
    private bool _isSelected;

    public void RefreshText() => OnPropertyChanged(nameof(Label));
}

/// <summary>A choice in a small picker, such as the tray icon coin.</summary>
public sealed partial class ChoiceOption : ObservableObject
{
    private readonly Func<string> _label;

    public ChoiceOption(string value, Func<string> label, Action<ChoiceOption> select)
    {
        Value = value;
        _label = label;
        SelectCommand = new RelayCommand(() => select(this));
    }

    public string Value { get; }
    public string Label => _label();
    public ICommand SelectCommand { get; }

    [ObservableProperty]
    private bool _isSelected;

    public void RefreshText() => OnPropertyChanged(nameof(Label));
}
