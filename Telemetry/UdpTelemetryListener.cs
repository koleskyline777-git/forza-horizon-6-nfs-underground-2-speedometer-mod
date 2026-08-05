using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;

namespace Nfsu2ForzaHud.Telemetry;

public sealed class UdpTelemetryListener : IAsyncDisposable
{
    private readonly Channel<TelemetryFrame> _channel =
        Channel.CreateBounded<TelemetryFrame>(new BoundedChannelOptions(2)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true
        });

    private CancellationTokenSource? _cts;
    private Task? _loop;
    private UdpClient? _udp;

    public int Port { get; private set; }
    public long PacketsReceived { get; private set; }
    public DateTime LastPacketUtc { get; private set; } = DateTime.MinValue;
    public ChannelReader<TelemetryFrame> Frames => _channel.Reader;

    public void Start(int port)
    {
        Stop();
        Port = port;
        _cts = new CancellationTokenSource();
        _udp = new UdpClient(new IPEndPoint(IPAddress.Any, port));
        _udp.Client.ReceiveBufferSize = 1024 * 64;
        _loop = Task.Run(() => ReceiveLoopAsync(_cts.Token));
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { /* ignore */ }
        try { _udp?.Close(); } catch { /* ignore */ }
        _udp = null;
        _cts = null;
        _loop = null;
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await _udp!.ReceiveAsync(ct);
                var frame = TelemetryFrame.TryParse(result.Buffer);
                if (frame is null) continue;
                PacketsReceived++;
                LastPacketUtc = DateTime.UtcNow;
                _channel.Writer.TryWrite(frame);
            }
            catch (OperationCanceledException) { break; }
            catch
            {
                await Task.Delay(50, ct);
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        Stop();
        return ValueTask.CompletedTask;
    }
}
