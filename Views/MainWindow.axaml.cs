using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using TuneVault.ViewModels;

namespace TuneVault.Views;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _progressTimer;
    private readonly MainWindowViewModel? _viewModel;

    public MainWindow()
    {
        InitializeComponent();

        _progressTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250),
        };
        _progressTimer.Tick += OnProgressTimerTick;
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
             _ = LoadLibraryAsync(_viewModel);
         }
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

     protected override void OnClosed(EventArgs e)
     {
         _progressTimer.Stop();
         _progressTimer.Tick -= OnProgressTimerTick;
         base.OnClosed(e);
     }
    private void OnProgressTimerTick(object? sender, EventArgs e)
    {
        _viewModel?.RefreshPlayback();
    }
}
