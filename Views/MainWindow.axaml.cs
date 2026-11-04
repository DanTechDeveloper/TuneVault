using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using TuneVault.ViewModels;

namespace TuneVault.Views;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _progressTimer;
    private readonly DispatcherTimer _toastTimer;
    private readonly MainWindowViewModel? _viewModel;

    public MainWindow()
    {
        InitializeComponent();

        _progressTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250),
        };
        _progressTimer.Tick += OnProgressTimerTick;

        _toastTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(4),
        };
        _toastTimer.Tick += OnToastTimerTick;
    }

    public MainWindow(MainWindowViewModel viewModel) : this()
    {
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _progressTimer.Start();

        if (_viewModel is not null)
        {
            _viewModel.Library.PropertyChanged += OnLibraryPropertyChangedForToast;
            _ = LoadLibraryAsync(_viewModel);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _progressTimer.Stop();
        _progressTimer.Tick -= OnProgressTimerTick;
        _toastTimer.Stop();
        _toastTimer.Tick -= OnToastTimerTick;
        if (_viewModel is not null)
        {
            _viewModel.Library.PropertyChanged -= OnLibraryPropertyChangedForToast;
        }
        base.OnClosed(e);
    }

    private void OnLibraryPropertyChangedForToast(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModels.LibraryViewModel.ShowToast) &&
            _viewModel?.Library.ShowToast == true)
        {
            _toastTimer.Stop();
            _toastTimer.Start();
        }
    }

    private void OnToastTimerTick(object? sender, EventArgs e)
    {
        _toastTimer.Stop();
        if (_viewModel is not null)
        {
            _viewModel.Library.CloseToastCommand.Execute(null);
        }
    }

    private void OnProgressTimerTick(object? sender, EventArgs e)
    {
        _viewModel?.RefreshPlayback();
    }

    private static async Task LoadLibraryAsync(MainWindowViewModel viewModel)
    {
        try
        {
            await viewModel.Library.LoadLibraryAsync();
        }
        catch
        {
        }
    }
}
