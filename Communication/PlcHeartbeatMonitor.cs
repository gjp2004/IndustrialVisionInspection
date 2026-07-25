using System.IO;

namespace IndustrialVisionStudent.Communication;

public sealed class PlcHeartbeatMonitor : IDisposable
{
    private readonly IPlcClient client;
    private CancellationTokenSource? cancellation;
    private Task? monitorTask;

    public PlcHeartbeatMonitor(IPlcClient client) =>
        this.client = client ?? throw new ArgumentNullException(nameof(client));
    public event Action<TimeSpan>? Succeeded;
    public event Action<Exception>? Failed;

    public void Start(TimeSpan interval)
    {
        Stop();
        cancellation = new CancellationTokenSource();
        monitorTask = Task.Run(() => LoopAsync(interval, cancellation.Token));
    }

    private async Task LoopAsync(TimeSpan interval, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(interval, token);
                DateTime started = DateTime.UtcNow;
                try
                {
                    string response = await client.SendRequestAsync("HEARTBEAT", TimeSpan.FromSeconds(2), token);
                    if (response != "ALIVE") throw new IOException($"心跳响应无效：{response}");
                    Succeeded?.Invoke(DateTime.UtcNow - started);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                catch (Exception exception) { Failed?.Invoke(exception); break; }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    public void Stop()
    {
        cancellation?.Cancel();
        try { monitorTask?.GetAwaiter().GetResult(); } catch { }
        cancellation?.Dispose(); cancellation = null; monitorTask = null;
    }

    public void Dispose() => Stop();
}
