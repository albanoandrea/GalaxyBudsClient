using System;
using System.Diagnostics;
using System.Threading;
using Android.OS;
using Avalonia.Threading;

namespace GalaxyBudsClient.Android.Fork;

/// <summary>
/// Avalonia's UI-thread dispatcher backend on the Android main Looper, replacing Avalonia.Android's
/// AndroidThreadingInterface + LegacyDispatcherImpl (Avalonia 11.3). There, every dispatcher OS timer is a new
/// System.Threading.Timer: .NET Timer thread -> thread-pool worker -> Handler.Post -> main thread (and the thread pool's
/// gate thread kept awake), and every DispatcherTimer restart costs an extra 1 ms "background processing" timer round
/// trip. With Avalonia's two never-ending 1 s compositor pool timers that is ~16 wake-ups/s while completely idle.
/// Here a timer is one delayed Looper message and background processing is a plain post. See Fork/README.md.
/// </summary>
/// <remarks>
/// Registered by <see cref="ForkStartupProvider"/> before Avalonia is set up: Dispatcher.UIThread is created lazily and
/// takes a bound IDispatcherImpl over the platform threading interface. Re-check on Avalonia upgrades.
/// </remarks>
internal sealed class ForkDispatcherImpl : IDispatcherImplWithExplicitBackgroundProcessing
{
    /// <summary>Off: stock Avalonia dispatcher (AndroidThreadingInterface + LegacyDispatcherImpl).</summary>
    public static readonly bool Enabled = true;

    private readonly Handler _handler = new(Looper.MainLooper!);
    private readonly Thread _mainThread;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    // One Runnable per role, reused: no Java peer allocated per post, and RemoveCallbacks(_timer) only removes timer posts
    private readonly Java.Lang.Runnable _signal;
    private readonly Java.Lang.Runnable _timer;
    private readonly Java.Lang.Runnable _background;
    private long? _timerDue; // UI thread only

    private ForkDispatcherImpl()
    {
        _mainThread = Thread.CurrentThread;
        _signal = new Java.Lang.Runnable(() => Signaled?.Invoke());
        _timer = new Java.Lang.Runnable(() =>
        {
            _timerDue = null;
            Timer?.Invoke();
        });
        _background = new Java.Lang.Runnable(() => ReadyForBackgroundProcessing?.Invoke());
    }

    /// <summary>
    /// Must run on the main thread before anything touches Dispatcher.UIThread. Only binds into AvaloniaLocator
    /// (a plain dictionary); doesn't touch the Dispatcher or any AvaloniaObject.
    /// </summary>
    public static void Register()
    {
        if (!Enabled)
            return;
        if (Looper.MyLooper() != Looper.MainLooper)
            throw new InvalidOperationException("ForkDispatcherImpl must be registered on the main thread");
        Avalonia.AvaloniaLocator.CurrentMutable.Bind<IDispatcherImpl>().ToConstant(new ForkDispatcherImpl());
    }

    // Managed compare, no JNI: CheckAccess/VerifyAccess runs on almost every AvaloniaObject access
    public bool CurrentThreadIsLoopThread => Thread.CurrentThread == _mainThread;

    // Same clock the Dispatcher computes timer due times from (DispatcherTimer.DueTimeInMs = Dispatcher.Now + interval)
    public long Now => _clock.ElapsedMilliseconds;

    public event Action? Signaled;
    public event Action? Timer;
    public event Action? ReadyForBackgroundProcessing;

    // Any thread. The Dispatcher coalesces signals itself (_signaled); a duplicate post just runs an empty queue pass.
    public void Signal() => _handler.Post(_signal);

    // UI thread. Runs after whatever is already queued on the Looper (input included), not on a 1 ms timer.
    public void RequestBackgroundProcessing() => _handler.Post(_background);

    // UI thread only (Dispatcher.UpdateOSTimer does VerifyAccess). Re-arms for the new due time, earlier or later;
    // null cancels. Due times in the past fire on the next Looper pass.
    public void UpdateTimer(long? dueTimeInMs)
    {
        if (_timerDue == dueTimeInMs)
            return;
        _handler.RemoveCallbacks(_timer);
        _timerDue = dueTimeInMs;
        if (dueTimeInMs is { } due)
            _handler.PostDelayed(_timer, Math.Max(0, due - Now));
    }
}
