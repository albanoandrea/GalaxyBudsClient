using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Android.Bluetooth;
using Android.Content;
using Android.Runtime;
using GalaxyBudsClient.Android.Utils;
using GalaxyBudsClient.Platform;
using GalaxyBudsClient.Platform.Interfaces;
using GalaxyBudsClient.Platform.Model;
using Java.Lang;
using Java.Util;
using Serilog;
using BluetoothDevice = GalaxyBudsClient.Platform.Model.BluetoothDevice;
using Exception = System.Exception;
using Thread = System.Threading.Thread;

#pragma warning disable CS0067

namespace GalaxyBudsClient.Android.Fork;

/// <summary>
/// Drop-in replacement for the upstream <see cref="Impl.BluetoothService"/> without polling
/// (see <see cref="ForkPlatformConfig.BlockingAndroidBackend"/>).
/// Each connection gets a reader thread blocked in <c>InputStream.read()</c> and a writer thread blocked on a queue,
/// so an idle link causes no wake-ups at all. The blocking read also detects a dead link (end of stream / IOException),
/// which replaces upstream's 2 s connection check timer.
/// Events, exception types and messages mirror upstream.
/// </summary>
public sealed class ForkBluetoothService : IBluetoothService
{
    private const string ConnectFailedMessage =
        "Connection failed. Your earbuds are either out of range or in use by the official manager app.";
    private const string ReadFailedMessage =
        "Connection closed. The earbuds are either out of range or in use by the official manager app.";
    private const string WriteFailedMessage = "Error while sending data. Connection to the earbuds has been closed.";

    public event EventHandler? RfcommConnected;
    public event EventHandler? Connecting;
    public event EventHandler? Connected;
    public event EventHandler<string>? Disconnected;
    public event EventHandler<BluetoothException>? BluetoothErrorAsync;
    public event EventHandler<byte[]>? NewDataAvailable;

    public bool IsStreamConnected { get; private set; }

    private readonly BluetoothAdapter _adapter;
    // Guards _socket and _link. _socket is the socket being connected or the connected one; _link is set once connected.
    private readonly object _lock = new();
    private BluetoothSocket? _socket;
    private Link? _link;
    private int _generation;

    public ForkBluetoothService(Context context)
    {
        var bluetoothManager = (BluetoothManager?)context.GetSystemService(Class.FromType(typeof(BluetoothManager)));
        var adapter = bluetoothManager?.Adapter;

        _adapter = adapter ?? throw new PlatformNotSupportedException("This device does not support Bluetooth");
    }

    private void RequireBluetoothEnabled()
    {
        if (!_adapter.IsEnabled)
        {
            throw new BluetoothException(BluetoothException.ErrorCodes.NoAdaptersAvailable,
                "Bluetooth is not enabled");
        }
    }

    #region Connection
    public async Task ConnectAsync(string macAddress, string uuid, CancellationToken cancelToken)
    {
        RequireBluetoothEnabled();

        // Like upstream: drop any previous connection first, silently
        CloseCurrent();

        Connecting?.Invoke(this, EventArgs.Empty);

        var device = _adapter.GetRemoteDevice(macAddress);
        if (device == null)
        {
            throw new BluetoothException(BluetoothException.ErrorCodes.ConnectFailed,
                "The selected Bluetooth device is not available. Please make sure it is paired and connected.");
        }

        var socket = device.CreateRfcommSocketToServiceRecord(UUID.FromString(uuid)) ??
                     throw new BluetoothException(BluetoothException.ErrorCodes.ConnectFailed,
                         "The selected Bluetooth device does not support Samsung's configuration protocol. " +
                         "Either the device is not a genuine Samsung Galaxy Buds device or the wrong model has been chosen during manual setup.");

        int generation;
        lock (_lock)
        {
            // Published before connecting, so that DisconnectAsync can abort a pending connect by closing it
            _socket = socket;
            generation = ++_generation;
        }

        Log.Debug("Connecting to socket for {MacAddress} with UUID {Uuid}...", macAddress, uuid);
        try
        {
            await socket.ConnectAsync();
        }
        catch (Exception ex) when (ex is Java.IO.IOException or IOException)
        {
            socket.CloseSafely();
            lock (_lock)
            {
                if (_socket == socket)
                    _socket = null;
            }

            Log.Error("Android.ForkBluetoothService: ConnectAsync: {ExMessage}", ex.Message);
            throw new BluetoothException(BluetoothException.ErrorCodes.ConnectFailed, ConnectFailedMessage);
        }

        Link link;
        lock (_lock)
        {
            if (_socket != socket || _generation != generation)
            {
                // Disconnected (or superseded by a newer connect) while connecting
                Log.Debug("Android.ForkBluetoothService: Connection superseded while connecting, closing it");
                socket.CloseSafely();
                throw new TaskCanceledException("Connection attempt superseded");
            }

            var input = ((InputStreamInvoker?)socket.InputStream)?.BaseInputStream;
            var output = ((OutputStreamInvoker?)socket.OutputStream)?.BaseOutputStream;
            if (input == null || output == null)
            {
                _socket = null;
                socket.CloseSafely();
                Log.Error("Android.ForkBluetoothService: ConnectAsync: socket has no streams");
                throw new BluetoothException(BluetoothException.ErrorCodes.ConnectFailed, ConnectFailedMessage);
            }

            link = new Link(socket, input, output);
            _link = link;
        }

        Log.Debug("Android.ForkBluetoothService: Connection established. Starting reader/writer threads");
        new Thread(() => ReaderLoop(link)) { IsBackground = true, Name = "Buds RFCOMM reader" }.Start();
        new Thread(() => WriterLoop(link)) { IsBackground = true, Name = "Buds RFCOMM writer" }.Start();

        IsStreamConnected = true;
        RfcommConnected?.Invoke(this, EventArgs.Empty);
    }
    #endregion

    #region Disconnection
    public Task DisconnectAsync()
    {
        Log.Debug("Android.ForkBluetoothService: Disconnecting...");
        // Requested by us: no Disconnected event (BluetoothImpl raises its own), like upstream
        CloseCurrent();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Closes the pending/current socket without raising <see cref="Disconnected"/>.
    /// Closing the socket unblocks the reader (and a pending connect); the writer exits once its queue is completed.
    /// </summary>
    private void CloseCurrent()
    {
        BluetoothSocket? socket;
        Link? link;
        lock (_lock)
        {
            socket = _socket;
            link = _link;
            _socket = null;
            _link = null;
            // Invalidates a connect still in progress, too
            _generation++;
        }

        link?.Close();
        socket?.CloseSafely();
    }

    /// <summary>
    /// Called by the reader/writer threads when the link broke on its own. Raises <see cref="Disconnected"/> at most once
    /// per connection, and only if this is still the current connection and we didn't close it ourselves.
    /// </summary>
    private void OnLinkLost(Link link, string reason)
    {
        lock (_lock)
        {
            if (link.IsClosed || _link != link)
                return;
            _link = null;
            if (_socket == link.Socket)
                _socket = null;
        }

        link.Close();
        IsStreamConnected = false;
        Disconnected?.Invoke(this, reason);
    }
    #endregion

    #region Transmission
    public Task SendAsync(byte[] data)
    {
        RequireBluetoothEnabled();

        Link? link;
        lock (_lock)
        {
            link = _link;
        }

        // Not connected: drop, so stale commands don't leak into the next connection
        if (link == null || !link.TryEnqueue(data))
        {
            Log.Debug("Android.ForkBluetoothService: SendAsync: not connected, dropping {Length} bytes", data.Length);
        }
        return Task.CompletedTask;
    }

    public Task<BluetoothDevice[]> GetDevicesAsync()
    {
        RequireBluetoothEnabled();

        return Task.FromResult(_adapter.BondedDevices?
            .Where(d => d.Address != null)
            .Select(d => new BluetoothDevice(d.Name ?? d.Address!, d.Address!,
                true /* connection state unknown */, true,
                new BluetoothCoD((uint?)d.BluetoothClass?.MajorDeviceClass ?? 0, 0),
                d
                    .GetUuids()?
                    .Where(u => u.Uuid != null)
                    .Select(u => Guid.Parse(u.Uuid!.ToString()))
                    .ToArray())
            )
            .ToArray() ?? []);
    }
    #endregion

    #region Threads
    private void ReaderLoop(Link link)
    {
        var buffer = new byte[2048];
        while (true)
        {
            int count;
            try
            {
                count = link.Input.Read(buffer, 0, buffer.Length);
            }
            catch (Exception ex) when (ex is Java.IO.IOException or IOException)
            {
                if (!link.IsClosed)
                    // Expected when the buds go away (case closed, out of range)
                    Log.Information("Android.ForkBluetoothService: ReaderLoop: read ended: {ExMessage}", ex.Message);
                count = -1;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Android.ForkBluetoothService: ReaderLoop: Unhandled exception");
                count = -1;
            }

            if (count < 0)
            {
                if (!link.IsClosed)
                    Log.Warning("Android.ForkBluetoothService: ReaderLoop: stream closed by the remote side");
                OnLinkLost(link, ReadFailedMessage);
                return;
            }

            if (count == 0)
                continue;

            // A stale reader of a connection we already dropped must not feed the new one
            if (link.IsClosed || Volatile.Read(ref _link) != link)
                return;

            IsStreamConnected = true;
            var data = new byte[count];
            Array.Copy(buffer, data, count);
            try
            {
                NewDataAvailable?.Invoke(this, data);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Android.ForkBluetoothService: ReaderLoop: NewDataAvailable handler failed");
            }
        }
    }

    private void WriterLoop(Link link)
    {
        try
        {
            foreach (var raw in link.Queue.GetConsumingEnumerable())
            {
                if (link.IsClosed)
                    return;

                try
                {
                    link.Output.Write(raw, 0, raw.Length);
                }
                catch (Exception ex) when (ex is Java.IO.IOException or IOException)
                {
                    if (!link.IsClosed)
                        Log.Error("Android.ForkBluetoothService: WriterLoop: Exception thrown while writing stream: {ExMessage}", ex.Message);
                    OnLinkLost(link, WriteFailedMessage);
                    return;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Android.ForkBluetoothService: WriterLoop: Unhandled exception");
                }
            }
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
        {
            // Queue completed/disposed by Close()
        }
    }
    #endregion

    /// <summary>State of one RFCOMM connection, owned by its reader and writer thread.</summary>
    private sealed class Link(BluetoothSocket socket, Java.IO.InputStream input, Java.IO.OutputStream output)
    {
        private int _closed;

        public BluetoothSocket Socket { get; } = socket;
        public Java.IO.InputStream Input { get; } = input;
        public Java.IO.OutputStream Output { get; } = output;
        public BlockingCollection<byte[]> Queue { get; } = new(new ConcurrentQueue<byte[]>());

        public bool IsClosed => Volatile.Read(ref _closed) != 0;

        public bool TryEnqueue(byte[] data)
        {
            if (IsClosed)
                return false;
            try
            {
                return Queue.TryAdd(data);
            }
            catch (InvalidOperationException)
            {
                // CompleteAdding() raced with us: connection is closing
                return false;
            }
        }

        /// <summary>Idempotent. Drops pending writes, stops the writer and closes the socket (which unblocks the reader).</summary>
        public void Close()
        {
            if (Interlocked.Exchange(ref _closed, 1) != 0)
                return;

            Queue.CompleteAdding();
            Socket.CloseSafely();
        }
    }
}
