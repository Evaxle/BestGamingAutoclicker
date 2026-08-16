using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using BestAutoClicker.ViewModels;

namespace BestAutoClicker.Views;

public partial class MainWindow : Window
{
    private MainViewModel? _vm;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => WireViewModel();
    }

    private void WireViewModel()
    {
        if (_vm != null) _vm.PropertyChanged -= OnViewModelPropertyChanged;
        _vm = DataContext as MainViewModel;
        if (_vm != null) _vm.PropertyChanged += OnViewModelPropertyChanged;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        RootPanel.Classes.Add("entrance");
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.IsSpamMode):
                if (_vm is { IsSpamMode: true }) ReplayAnimation(SpamCard, "cardIn");
                break;
            case nameof(MainViewModel.IsHoldMode):
                if (_vm is { IsHoldMode: true }) ReplayAnimation(HoldCard, "cardIn");
                break;
            case nameof(MainViewModel.IsAdvancedOpen):
                if (_vm is { IsAdvancedOpen: true })
                {
                    AdvancedPanel.IsVisible = true;
                    ReplayAnimation(AdvancedPanel, "panelIn");
                }
                else
                {
                    AdvancedPanel.IsVisible = false;
                }
                break;
        }
    }

    private static void ReplayAnimation(Control target, string className)
    {
        target.Classes.Remove(className);
        Dispatcher.UIThread.Post(() => target.Classes.Add(className));
    }

    private void DragRegion_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void MinimizeButton_OnClick(object? sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void CloseButton_OnClick(object? sender, RoutedEventArgs e)
        => Close();
}
