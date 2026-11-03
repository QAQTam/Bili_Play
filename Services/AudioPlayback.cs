using BiliPlay.Core;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace BiliPlay.Services;

// Each deck owns its relay and decoder. Preparing a new quality never invalidates the live URL.
internal sealed class AudioPlayback : IDisposable
{
    public MediaPlayer Player { get; } = new() { AutoPlay = false, AudioCategory = MediaPlayerAudioCategory.Media };
    private double _playbackRate = 1;
    private bool _disposed;
    public double PlaybackRate
    {
        get => _playbackRate;
        set
        {
            _playbackRate = PlaybackSpeed.Validate(value);
            ApplyPlaybackRate();
        }
    }
    public AudioProxy Proxy { get; }
    public MediaPlaybackItem? Item { get; set; }
    private readonly TaskCompletionSource<Exception> _failure = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public AudioPlayback(HttpMessageHandler? handler = null)
    {
        Proxy = new(handler);
        Player.CommandManager.IsEnabled = false; Player.SystemMediaTransportControls.IsEnabled = false;
        Player.MediaOpened += (_, _) => ApplyPlaybackRate();
        Player.PlaybackSession.PlaybackStateChanged += (_, _) => ApplyPlaybackRate();
        Player.MediaFailed += (_, error) => _failure.TrySetResult(new IOException("新音质音源无法解码：" + error.ErrorMessage));
    }
    private void ApplyPlaybackRate()
    {
        if (_disposed) return;
        var session = Player.PlaybackSession;
        if (session.PlaybackState is MediaPlaybackState.Playing or MediaPlaybackState.Paused
            && Math.Abs(session.PlaybackRate - _playbackRate) > .001) session.PlaybackRate = _playbackRate;
    }
    public void SetSource(AudioStream stream)
    {
        Player.Pause(); Player.Source = null; Item?.Source.Dispose(); Item = null;
        Item = new(MediaSource.CreateFromUri(Proxy.SetSource(stream))); Player.Source = Item;
    }
    private async Task WaitAsync(Task signal, CancellationToken token)
    {
        var completed = await Task.WhenAny(signal, _failure.Task).WaitAsync(token);
        if (completed == _failure.Task) throw await _failure.Task;
        await signal;
    }
    public async Task WarmMutedAsync(AudioStream stream, CancellationToken token)
    {
        Player.Volume = 0;
        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Opened(MediaPlayer sender, object args) => opened.TrySetResult();
        Player.MediaOpened += Opened;
        try { SetSource(stream); await WaitAsync(opened.Task, token); }
        finally { Player.MediaOpened -= Opened; }
        await WaitUntilPlayingAsync(token);
    }
    private async Task WaitUntilPlayingAsync(CancellationToken token)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void StateChanged(MediaPlaybackSession sender, object args)
        { if (sender.PlaybackState == MediaPlaybackState.Playing) ready.TrySetResult(); }
        Player.PlaybackSession.PlaybackStateChanged += StateChanged;
        try
        {
            Player.Play();
            if (Player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing) ready.TrySetResult();
            await WaitAsync(ready.Task, token);
        }
        finally { Player.PlaybackSession.PlaybackStateChanged -= StateChanged; }
    }
    public async Task AlignToAsync(AudioPlayback live, CancellationToken token)
    {
        PlaybackRate = live.PlaybackRate;
        // Sample the live position after loading, including any user seeks while preparing.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            token.ThrowIfCancellationRequested();
            var target = live.Player.PlaybackSession.Position;
            if (Math.Abs((Player.PlaybackSession.Position - target).TotalSeconds) <= .12) break;
            var sought = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void SeekCompleted(MediaPlaybackSession sender, object args) => sought.TrySetResult();
            Player.PlaybackSession.SeekCompleted += SeekCompleted;
            try { Player.PlaybackSession.Position = target; await WaitAsync(sought.Task, token); }
            finally { Player.PlaybackSession.SeekCompleted -= SeekCompleted; }
        }
        if (live.Player.PlaybackSession.PlaybackState != MediaPlaybackState.Playing) Player.Pause();
    }
    public bool ActivateInsteadOf(AudioPlayback live)
    {
        PlaybackRate = live.PlaybackRate;
        var playing = live.Player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;
        var volume = live.Player.Volume;
        if (!playing) Player.Pause();
        live.Player.Volume = 0; live.Player.Pause(); Player.Volume = volume;
        if (playing) Player.Play();
        return playing;
    }
    public void Dispose()
    {
        _disposed = true;
        Player.Pause(); Player.Source = null; Item?.Source.Dispose(); Item = null;
        Player.Dispose(); Proxy.Dispose();
    }
}
