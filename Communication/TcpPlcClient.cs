using System.Net.Sockets;
using System.Text;
using System.IO;

namespace IndustrialVisionStudent.Communication;

public sealed class TcpPlcClient : IPlcClient
{
    private readonly SemaphoreSlim requestLock = new(1, 1);
    private readonly object stateLock = new();
    private TcpClient? client;
    private StreamReader? reader;
    private StreamWriter? writer;
    private CancellationTokenSource? receiveCancellation;
    private Task? receiveTask;
    private TaskCompletionSource<string>? pendingResponse;
    private readonly HashSet<string> receivedCycleIds = new(StringComparer.Ordinal);
    private readonly Queue<string> receivedCycleOrder = new();
    private const int MaximumRememberedCycles = 1000;

    public event Action<string>? StartRequested;
    public event Action<Exception>? ConnectionLost;

    public bool IsConnected
    {
        get { lock (stateLock) return client?.Connected == true; }
    }

    public async Task ConnectAsync(string host, int port, TimeSpan timeout, CancellationToken token = default)
    {
        await DisconnectAsync();
        var newClient = new TcpClient { NoDelay = true };
        try
        {
            await newClient.ConnectAsync(host, port, token).AsTask().WaitAsync(timeout, token);
            NetworkStream stream = newClient.GetStream();
            var newReader = new StreamReader(stream, new UTF8Encoding(false), false, 1024, true);
            var newWriter = new StreamWriter(stream, new UTF8Encoding(false), 1024, true)
            {
                AutoFlush = true,
                NewLine = "\n"
            };
            lock (stateLock)
            {
                client = newClient; reader = newReader; writer = newWriter;
                receiveCancellation = new CancellationTokenSource();
                receiveTask = Task.Run(() => ReceiveLoopAsync(receiveCancellation.Token));
            }
        }
        catch
        {
            newClient.Dispose();
            throw;
        }
    }

    public async Task<string> SendRequestAsync(
        string request, TimeSpan timeout, CancellationToken token = default)
    {
        await requestLock.WaitAsync(token);
        try
        {
            StreamWriter activeWriter;
            var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (stateLock)
            {
                activeWriter = writer ?? throw new InvalidOperationException("PLC尚未连接。");
                pendingResponse = completion;
            }

            try
            {
                await activeWriter.WriteLineAsync(request.AsMemory(), token);
                return await completion.Task.WaitAsync(timeout, token);
            }
            finally
            {
                lock (stateLock)
                    if (ReferenceEquals(pendingResponse, completion)) pendingResponse = null;
            }
        }
        finally
        {
            requestLock.Release();
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                StreamReader activeReader;
                lock (stateLock) activeReader = reader ?? throw new IOException("PLC连接已关闭。");
                string? line = await activeReader.ReadLineAsync(token);
                if (line is null) throw new IOException("PLC已断开连接。");
                line = line.Trim();
                if (line.StartsWith("START ", StringComparison.OrdinalIgnoreCase))
                {
                    string cycleId = line[6..].Trim();
                    if (cycleId.Length > 0 && TryRegisterCycle(cycleId))
                        StartRequested?.Invoke(cycleId);
                    continue;
                }

                TaskCompletionSource<string>? completion;
                lock (stateLock) completion = pendingResponse;
                completion?.TrySetResult(line);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            TaskCompletionSource<string>? completion;
            lock (stateLock) completion = pendingResponse;
            completion?.TrySetException(exception);
            ConnectionLost?.Invoke(exception);
        }
    }

    private bool TryRegisterCycle(string cycleId)
    {
        lock (stateLock)
        {
            if (!receivedCycleIds.Add(cycleId)) return false;
            receivedCycleOrder.Enqueue(cycleId);
            while (receivedCycleOrder.Count > MaximumRememberedCycles)
                receivedCycleIds.Remove(receivedCycleOrder.Dequeue());
            return true;
        }
    }

    public async Task DisconnectAsync()
    {
        CancellationTokenSource? cancellation;
        Task? task;
        lock (stateLock)
        {
            cancellation = receiveCancellation;
            task = receiveTask;
            receiveCancellation = null; receiveTask = null;
            cancellation?.Cancel();
            client?.Close();
        }
        if (task is not null)
        {
            try { await task; } catch { }
        }
        lock (stateLock)
        {
            reader?.Dispose(); writer?.Dispose(); client?.Dispose();
            reader = null; writer = null; client = null;
            pendingResponse?.TrySetCanceled(); pendingResponse = null;
        }
        cancellation?.Dispose();
    }

    public void Dispose()
    {
        DisconnectAsync().GetAwaiter().GetResult();
        requestLock.Dispose();
    }
}
