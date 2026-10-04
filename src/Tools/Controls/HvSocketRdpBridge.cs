using System.Net;
using System.Net.Sockets;
using ExHyperV.Services;

namespace ExHyperV.Tools;

// A single-use loopback endpoint for the embedded RDP control. The guest side
// uses VMBus exclusively; it does not expose a TCP port on the guest network.
internal sealed class HvSocketRdpBridge : IDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource cancel = new();
    private readonly Guid vmId;
    private TcpClient? client;
    private Socket? guest;
    internal int Port { get; }

    internal HvSocketRdpBridge(string vmId)
    {
        this.vmId = Guid.Parse(vmId);
        listener.Start(1);
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        _ = Task.Run(RunAsync);
    }

    private async Task RunAsync()
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            client = await listener.AcceptTcpClientAsync(timeout.Token);
            listener.Stop(); // Never accept a second connection.
            client.NoDelay = true;
            guest = new Socket((AddressFamily)34, SocketType.Stream, (ProtocolType)1);
            using var abort = timeout.Token.Register(() => guest.Dispose());
            await Task.Run(() => guest.Connect(new HvEndpoint(vmId)), timeout.Token);
            timeout.CancelAfter(Timeout.InfiniteTimeSpan);
            AutoConnectLog.Write("Enhanced-session VMBus transport connected.");
            using var remote = new NetworkStream(guest, ownsSocket: false);
            using var local = client.GetStream();
            var upstream = local.CopyToAsync(remote, cancel.Token);
            var downstream = remote.CopyToAsync(local, cancel.Token);
            await Task.WhenAny(upstream, downstream);
            Dispose();
            try { await Task.WhenAll(upstream, downstream); } catch { }
        }
        catch (Exception ex)
        {
            if (!cancel.IsCancellationRequested)
                AutoConnectLog.Write("Enhanced-session VMBus transport failed: " + ex.GetType().Name);
        }
        finally { Dispose(); }
    }

    public void Dispose()
    {
        cancel.Cancel();
        listener.Stop();
        client?.Dispose();
        guest?.Dispose();
    }

    private sealed class HvEndpoint(Guid vmId) : EndPoint
    {
        public override AddressFamily AddressFamily => (AddressFamily)34;
        public override SocketAddress Serialize()
        {
            var address = new SocketAddress(AddressFamily, 36);
            var vm = vmId.ToByteArray();
            var service = new Guid("00000d3d-facb-11e6-bd58-64006a7986d3").ToByteArray();
            for (int i = 0; i < 16; i++) { address[i + 4] = vm[i]; address[i + 20] = service[i]; }
            return address;
        }
        public override EndPoint Create(SocketAddress socketAddress) => this;
    }
}
