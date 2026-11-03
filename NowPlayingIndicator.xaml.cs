using System.ComponentModel;
using System.Numerics;
using BiliPlay.Core;
using BiliPlay.ViewModels;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Windows.UI.ViewManagement;

namespace BiliPlay;

// One shared marker for every track surface. Track identity is BVID + part, not object identity.
public sealed partial class NowPlayingIndicator : UserControl
{
    public static readonly DependencyProperty TrackProperty = DependencyProperty.Register(nameof(Track), typeof(Track), typeof(NowPlayingIndicator),
        new PropertyMetadata(null, (sender, _) => ((NowPlayingIndicator)sender).Refresh()));
    public Track? Track { get => (Track?)GetValue(TrackProperty); set => SetValue(TrackProperty, value); }
    private PlayerViewModel? _viewModel;
    private readonly UISettings _settings = new();
    private bool _animating;
    private bool _inViewport = true;
    public bool IsCurrent => WaveButton.Visibility == Visibility.Visible;
    public NowPlayingIndicator()
    {
        InitializeComponent();
        Unloaded += (_, _) => Detach();
        EffectiveViewportChanged += (_, args) => { _inViewport = args.EffectiveViewport.Width > 0 && args.EffectiveViewport.Height > 0; Refresh(); };
        SizeChanged += (_, _) => Refresh();
        Bars.SizeChanged += (_, _) => { StopBars(); Refresh(); };
    }
    public void Attach(PlayerViewModel viewModel)
    {
        if (_viewModel != viewModel)
        {
            Detach(); _viewModel = viewModel;
            viewModel.PropertyChanged += OnChanged;
            viewModel.Player.Changed += PlaybackChanged;
        }
        Refresh();
    }
    private void Detach()
    {
        if (_viewModel is { } model) { model.PropertyChanged -= OnChanged; model.Player.Changed -= PlaybackChanged; }
        _viewModel = null; StopBars();
    }
    private void OnChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(PlayerViewModel.CurrentTrack) or nameof(PlayerViewModel.PlayGlyph)) Refresh();
    }
    private void PlaybackChanged() => Refresh();
    private void Refresh()
    {
        if (WaveButton is null) return;
        var current = QueueSelection.Same(Track, _viewModel?.CurrentTrack);
        WaveButton.Visibility = current ? Visibility.Visible : Visibility.Collapsed;
        var playing = current && _viewModel?.Player.Playing == true;
        var label = playing ? "正在播放，点击暂停" : "当前曲目，点击播放";
        AutomationProperties.SetName(WaveButton, label);
        if (!playing || !_inViewport || !_settings.AnimationsEnabled) { StopBars(); return; }
        if (_animating) return;
        _animating = true;
        for (var i = 0; i < Bars.Children.Count; i++)
        {
            var bar = (FrameworkElement)Bars.Children[i];
            var visual = ElementCompositionPreview.GetElementVisual(bar);
            visual.CenterPoint = new((float)bar.ActualWidth / 2, (float)bar.ActualHeight / 2, 0);
            var wave = visual.Compositor.CreateVector3KeyFrameAnimation();
            wave.InsertKeyFrame(0, new(1, .4f + i * .1f, 1));
            wave.InsertKeyFrame(.35f, new(1, 1, 1));
            wave.InsertKeyFrame(.7f, new(1, .25f, 1));
            wave.InsertKeyFrame(1, new(1, .4f + i * .1f, 1));
            wave.Duration = TimeSpan.FromMilliseconds(720 + i * 110);
            wave.IterationBehavior = AnimationIterationBehavior.Forever;
            visual.StartAnimation("Scale", wave);
        }
    }
    private void StopBars()
    {
        _animating = false;
        if (Bars is null) return;
        foreach (var bar in Bars.Children)
        {
            var visual = ElementCompositionPreview.GetElementVisual(bar);
            visual.StopAnimation("Scale"); visual.Scale = Vector3.One;
        }
    }
    private async void WaveClick(object sender, RoutedEventArgs args)
    {
        if (_viewModel is { } model && IsCurrent) await model.TogglePlayCommand.ExecuteAsync(null);
    }
}
