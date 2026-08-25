using System.Windows;
using System.Windows.Input;
using FloatWeather.ViewModels;

namespace FloatWeather;

/// <summary>详情主窗口</summary>
public partial class MainWindow : Window
{
    private readonly DetailViewModel _vm;

    public MainWindow(DetailViewModel vm)
    {
        InitializeComponent();
        DataContext = _vm = vm;
        Loaded += async (_, _) => await _vm.LoadAsync();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch { /* 忽略拖拽异常 */ }
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
