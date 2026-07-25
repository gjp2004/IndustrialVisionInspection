using System.Net;
using System.Net.Sockets;
using IndustrialVisionStudent.Communication;

namespace IndustrialVisionStudent.Tests;

public sealed class PlcIntegrationTests
{
    [Fact]
    public async Task SimulatorAndClient_CompletePingAndInspectionHandshake()
    {
        int port = GetFreePort();
        using var server = new SimulatedPlcServer();
        using var client = new TcpPlcClient();
        var startReceived = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.StartRequested += id => startReceived.TrySetResult(id);

        server.Start(port);
        await client.ConnectAsync("127.0.0.1", port, TimeSpan.FromSeconds(2));
        await WaitUntilAsync(() => server.HasClient, TimeSpan.FromSeconds(2));
        Assert.Equal("PONG", await client.SendRequestAsync("PING", TimeSpan.FromSeconds(2)));

        await server.TriggerInspectionAsync("1001");
        Assert.Equal("1001", await startReceived.Task.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal("ACK BUSY 1001",
            await client.SendRequestAsync("BUSY 1001", TimeSpan.FromSeconds(2)));
        Assert.Equal("ACK RESULT 1001",
            await client.SendRequestAsync("RESULT 1001 OK", TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task HeartbeatMonitor_ReportsHealthyLink()
    {
        int port = GetFreePort();
        using var server = new SimulatedPlcServer();
        using var client = new TcpPlcClient();
        using var monitor = new PlcHeartbeatMonitor(client);
        var succeeded = new TaskCompletionSource<TimeSpan>(TaskCreationOptions.RunContinuationsAsynchronously);
        monitor.Succeeded += elapsed => succeeded.TrySetResult(elapsed);
        server.Start(port);
        await client.ConnectAsync("127.0.0.1", port, TimeSpan.FromSeconds(2));
        monitor.Start(TimeSpan.FromMilliseconds(30));
        Assert.True((await succeeded.Task.WaitAsync(TimeSpan.FromSeconds(2))) >= TimeSpan.Zero);
    }

    [Fact]
    public async Task HeartbeatMonitor_WorksThroughPlcClientAbstraction()
    {
        using var client = new HealthyFakePlcClient();
        using var monitor = new PlcHeartbeatMonitor(client);
        var succeeded = new TaskCompletionSource<TimeSpan>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        monitor.Succeeded += elapsed => succeeded.TrySetResult(elapsed);

        monitor.Start(TimeSpan.FromMilliseconds(10));

        await succeeded.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(client.RequestCount > 0);
    }

    [Fact]
    public async Task DuplicateStartCycle_IsRaisedOnlyOnce()
    {
        int port = GetFreePort();
        using var server = new SimulatedPlcServer();
        using var client = new TcpPlcClient();
        int received = 0;
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.StartRequested += _ =>
        {
            Interlocked.Increment(ref received);
            first.TrySetResult();
        };

        server.Start(port);
        await client.ConnectAsync("127.0.0.1", port, TimeSpan.FromSeconds(2));
        await WaitUntilAsync(() => server.HasClient, TimeSpan.FromSeconds(2));
        await server.TriggerInspectionAsync("DUPLICATE-1");
        await server.TriggerInspectionAsync("DUPLICATE-1");
        await first.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(100);

        Assert.Equal(1, Volatile.Read(ref received));
    }

    [Fact]
    public async Task DuplicateStartAfterReconnect_IsStillRaisedOnlyOnce()
    {
        int port = GetFreePort();
        using var server = new SimulatedPlcServer();
        using var client = new TcpPlcClient();
        int received = 0;
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.StartRequested += _ =>
        {
            Interlocked.Increment(ref received);
            first.TrySetResult();
        };

        server.Start(port);
        await client.ConnectAsync("127.0.0.1", port, TimeSpan.FromSeconds(2));
        await WaitUntilAsync(() => server.HasClient, TimeSpan.FromSeconds(2));
        await server.TriggerInspectionAsync("RECONNECT-1");
        await first.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await client.DisconnectAsync();
        await client.ConnectAsync("127.0.0.1", port, TimeSpan.FromSeconds(2));
        await WaitUntilAsync(() => server.HasClient, TimeSpan.FromSeconds(2));
        await server.TriggerInspectionAsync("RECONNECT-1");
        await Task.Delay(100);

        Assert.Equal(1, Volatile.Read(ref received));
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(); int port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException();
            await Task.Delay(10);
        }
    }

    private sealed class HealthyFakePlcClient : IPlcClient
    {
        public event Action<string>? StartRequested
        {
            add { }
            remove { }
        }

        public event Action<Exception>? ConnectionLost
        {
            add { }
            remove { }
        }

        public bool IsConnected => true;
        public int RequestCount { get; private set; }

        public Task ConnectAsync(
            string host, int port, TimeSpan timeout, CancellationToken token = default) =>
            Task.CompletedTask;

        public Task<string> SendRequestAsync(
            string request, TimeSpan timeout, CancellationToken token = default)
        {
            RequestCount++;
            return Task.FromResult("ALIVE");
        }

        public Task DisconnectAsync() => Task.CompletedTask;
        public void Dispose() { }
    }
}
