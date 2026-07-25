namespace IndustrialVisionStudent.Communication;

public interface IPlcClient : IDisposable
{
    event Action<string>? StartRequested;
    event Action<Exception>? ConnectionLost;

    bool IsConnected { get; }

    Task ConnectAsync(
        string host,
        int port,
        TimeSpan timeout,
        CancellationToken token = default);

    Task<string> SendRequestAsync(
        string request,
        TimeSpan timeout,
        CancellationToken token = default);

    Task DisconnectAsync();
}
