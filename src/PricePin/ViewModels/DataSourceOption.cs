using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PricePin.Core.Market;

namespace PricePin.ViewModels;

/// <summary>One entry in the "Data source" menu.</summary>
public sealed partial class DataSourceOption : ObservableObject
{
    public DataSourceOption(DataSource value, string label, Action<DataSourceOption> select)
    {
        Value = value;
        Label = label;
        SelectCommand = new RelayCommand(() => select(this));
    }

    public DataSource Value { get; }
    public string Label { get; }
    public ICommand SelectCommand { get; }

    [ObservableProperty]
    private bool _isSelected;
}
