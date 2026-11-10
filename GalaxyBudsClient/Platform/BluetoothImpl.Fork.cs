using System;
using System.Threading;
using System.Threading.Tasks;

namespace GalaxyBudsClient.Platform;

/// <summary>
/// Fork: event-driven <see cref="DataConsumerLoop"/> (see <see cref="ForkConfig.EventDrivenDataConsumer"/>).
/// Upstream wakes the consumer every 50 ms even while nothing arrives; here it sleeps until a frame is queued.
/// </summary>
public sealed partial class BluetoothImpl
{
    // Static like IncomingQueue, so it survives Reallocate(). One release per enqueued frame.
    private static readonly SemaphoreSlim IncomingSignal = new(0);

    /// <summary>Called right after a frame was added to <see cref="IncomingQueue"/>.</summary>
    private static void SignalIncomingData()
    {
        if (ForkConfig.EventDrivenDataConsumer)
            IncomingSignal.Release();
    }

    /// <summary>
    /// Replaces upstream's <c>Task.Delay(50).Wait(token)</c>: blocks until a frame was queued, then still waits 50 ms
    /// so a message split over several reads arrives completely before decoding (an incomplete frame counts as a
    /// broken packet and triggers a reconnect). Throws <see cref="OperationCanceledException"/> on cancellation, like upstream.
    /// </summary>
    private static void WaitForIncomingData(CancellationToken token)
    {
        if (!ForkConfig.EventDrivenDataConsumer)
        {
            Task.Delay(50).Wait(token);
            return;
        }

        IncomingSignal.Wait(token);
        try
        {
            Task.Delay(50).Wait(token);
        }
        catch (OperationCanceledException)
        {
            // We took a signal but won't drain: hand it back so the consumer of a reallocated instance still sees the data
            IncomingSignal.Release();
            throw;
        }

        // The caller drains the whole queue next, which covers every frame signalled so far: drop the surplus signals
        // so a burst doesn't cause a series of empty iterations. Frames enqueued after this point signal again.
        while (IncomingSignal.Wait(0))
        {
        }
    }
}
