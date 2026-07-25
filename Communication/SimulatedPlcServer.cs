using System.Net;
using System.Net.Sockets;
using System.Text;
using System.IO;

namespace IndustrialVisionStudent.Communication;

public sealed class SimulatedPlcServer : IDisposable
{
    private readonly SemaphoreSlim sendLock = new(1, 1);
    private readonly object stateLock = new();
    private TcpListener? listener;
    private TcpClient? connectedClient;
    private StreamWriter? writer;
    private CancellationTokenSource? cancellation;
    private Task? serverTask;

    public event Action<string>? LogReceived;
    public bool IsRunning { get; private set; }
    public bool HasClient { get { lock (stateLock) return connectedClient?.Connected == true; } }

    public void Start(int port)
    {
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        if (IsRunning) return;
        listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        cancellation = new CancellationTokenSource();
        IsRunning = true;
        serverTask = Task.Run(() => AcceptLoopAsync(cancellation.Token));
        LogReceived?.Invoke($"PLC模拟器已监听127.0.0.1:{port}");
    }

    private async Task AcceptLoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                TcpClient client = await listener!.AcceptTcpClientAsync(token);
                lock (stateLock)
                {
                    connectedClient?.Dispose();
                    connectedClient = client;
                }
                await HandleClientAsync(client, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception) { LogReceived?.Invoke($"PLC模拟器异常：{exception.Message}"); }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken token)
    {
        client.NoDelay = true;
        using NetworkStream stream = client.GetStream();
        using var reader = new StreamReader(stream, new UTF8Encoding(false), false, 1024, true);
        using var activeWriter = new StreamWriter(stream, new UTF8Encoding(false), 1024, true)
        { AutoFlush = true, NewLine = "\n" };
        lock (stateLock) writer = activeWriter;
        LogReceived?.Invoke("上位机已连接PLC模拟器");
        try
        {
            while (!token.IsCancellationRequested)
            {
                string? line = await reader.ReadLineAsync(token);
                if (line is null) break;
                line = line.Trim();
                LogReceived?.Invoke($"PLC收到：{line}");
                string response = CreateResponse(line);
                await SendAsync(response, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (IOException) { }
        finally
        {
            lock (stateLock)
            {
                if (ReferenceEquals(connectedClient, client))
                { connectedClient = null; writer = null; }
            }
            client.Dispose();
            LogReceived?.Invoke("上位机已断开PLC模拟器");
        }
    }

    private static string CreateResponse(string request)
    {
        if (request.Equals("PING", StringComparison.OrdinalIgnoreCase)) return "PONG";
        if (request.Equals("HEARTBEAT", StringComparison.OrdinalIgnoreCase)) return "ALIVE";
        string[] parts = request.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2 && parts[0].Equals("BUSY", StringComparison.OrdinalIgnoreCase))
            return $"ACK BUSY {parts[1]}";
        if (parts.Length == 3 && parts[0].Equals("RESULT", StringComparison.OrdinalIgnoreCase) &&
            parts[2] is "OK" or "NG") return $"ACK RESULT {parts[1]}";
        return "ERR UNKNOWN_COMMAND";
    }

    public Task TriggerInspectionAsync(string cycleId, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(cycleId)) throw new ArgumentException("周期号不能为空。", nameof(cycleId));
        return SendAsync($"START {cycleId.Trim()}", token);
    }

    private async Task SendAsync(string message, CancellationToken token)
    {
        await sendLock.WaitAsync(token);
        try
        {
            StreamWriter activeWriter;
            lock (stateLock) activeWriter = writer ?? throw new InvalidOperationException("尚无上位机连接PLC模拟器。");
            await activeWriter.WriteLineAsync(message.AsMemory(), token);
            LogReceived?.Invoke($"PLC发送：{message}");
        }
        finally { sendLock.Release(); }
    }

    public void Stop()
    {
        if (!IsRunning) return;
        cancellation?.Cancel(); listener?.Stop(); connectedClient?.Close();
        try { serverTask?.GetAwaiter().GetResult(); } catch { }
        cancellation?.Dispose(); cancellation = null; listener = null; serverTask = null;
        lock (stateLock) { connectedClient?.Dispose(); connectedClient = null; writer = null; }
        IsRunning = false;
    }

    public void Dispose() { Stop(); sendLock.Dispose(); }
}
