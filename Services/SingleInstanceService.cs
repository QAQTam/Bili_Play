using System.Security.Principal;

namespace BiliPlay.Services;

/// <summary>A per-user, per-profile process gate with an activation signal.</summary>
public sealed class SingleInstanceService : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activation;
    private RegisteredWaitHandle? _wait;
    private bool _disposed;
    public bool IsPrimary { get; }

    public SingleInstanceService(Action activate, string? scope = null)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var user = identity.User?.Value ?? Environment.UserName;
        var key = $@"Local\BiliPlay.{scope ?? AppProfile.InstanceScope}.{user}";
        _mutex = new(false, key + ".Mutex");
        _activation = new(false, EventResetMode.AutoReset, key + ".Activate");
        try { IsPrimary = _mutex.WaitOne(0); }
        catch (AbandonedMutexException) { IsPrimary = true; }
        if (IsPrimary)
            _wait = ThreadPool.RegisterWaitForSingleObject(_activation, (_, _) => activate(), null, Timeout.Infinite, false);
        else _activation.Set();
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _wait?.Unregister(null); _activation.Dispose();
        if (IsPrimary) _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
