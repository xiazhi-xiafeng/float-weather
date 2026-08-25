using System.Collections.ObjectModel;
using System.Windows;
using FloatWeather.ViewModels;

namespace FloatWeather.Views;

/// <summary>数据源配置弹窗：展示一组字段，确定后返回填写值</summary>
public partial class ProviderDialogWindow : Window
{
    private readonly ObservableCollection<ProviderFieldRow> _fields = new();

    public string DialogTitle { get; }
    public ObservableCollection<ProviderFieldRow> Fields => _fields;
    public IReadOnlyList<string> Values => _fields.Select(f => f.Value).ToList();

    public ProviderDialogWindow(string title, IEnumerable<ProviderFieldRow> fields)
    {
        DialogTitle = title;
        foreach (var f in fields) _fields.Add(f);
        InitializeComponent();
        DataContext = this;
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
