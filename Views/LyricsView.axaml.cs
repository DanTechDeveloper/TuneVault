using System;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using TuneVault.ViewModels;

namespace TuneVault.Views;

public partial class LyricsView : UserControl
{
    private PlaybackViewModel? _playback;
    private ScrollViewer? _scrollViewer;
    private bool _anchorPending;

    public LyricsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        LayoutUpdated += OnLayoutUpdated;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_playback is not null)
        {
            _playback.PropertyChanged -= OnPlaybackPropertyChanged;
        }

        _playback = (DataContext as MainWindowViewModel)?.Playback;
        _scrollViewer = null;

        if (_playback is not null)
        {
            _playback.PropertyChanged += OnPlaybackPropertyChanged;
        }
    }

    private void OnPlaybackPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PlaybackViewModel.ActiveLyricLine)
            or nameof(PlaybackViewModel.HasLyrics)
            or nameof(PlaybackViewModel.IsLyricsView))
        {
            AnchorActiveLine();
        }
    }

    private void AnchorActiveLine()
    {
        _scrollViewer ??= LyricsList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (_scrollViewer is null)
        {
            return;
        }

        if (_playback?.ActiveLyricLine is not { } line || !_playback.Lyrics.Contains(line))
        {
            _scrollViewer.Offset = new Vector(0, 0);
            return;
        }

        LyricsList.ScrollIntoView(line);
        _anchorPending = true;
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (!_anchorPending || _scrollViewer is null || _playback?.ActiveLyricLine is not { } line)
        {
            return;
        }

        var index = _playback.Lyrics.IndexOf(line);
        if (index < 0)
        {
            return;
        }

        var container = LyricsList.ContainerFromIndex(index);
        if (container?.TranslatePoint(new Point(0, 0), _scrollViewer) is not { } point)
        {
            return;
        }

        _anchorPending = false;
        var anchorPx = _scrollViewer.Viewport.Height * 0.4;
        var maxOffset = Math.Max(0, _scrollViewer.Extent.Height - _scrollViewer.Viewport.Height);
        _scrollViewer.Offset = new Vector(
            _scrollViewer.Offset.X,
            AnchorOffset(_scrollViewer.Offset.Y, point.Y, anchorPx, maxOffset));
    }

    // Pure math so the anchor can be checked without a rendered view.
    public static double AnchorOffset(double currentOffset, double itemTop, double anchorPx, double maxOffset) =>
        Math.Clamp(currentOffset + itemTop - anchorPx, 0, maxOffset);
}
