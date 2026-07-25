# PLC适配接口说明

## 当前状态

当前项目使用`TcpPlcClient`实现教学用文本TCP协议。它不是Modbus TCP。

业务层和心跳监控现在依赖`IPlcClient`接口，因此接入真实PLC时可以增加新的适配器，而不修改视觉检测、历史记录和自动模式主流程。

## 接口职责

```csharp
public interface IPlcClient : IDisposable
{
    event Action<string>? StartRequested;
    event Action<Exception>? ConnectionLost;
    bool IsConnected { get; }
    Task ConnectAsync(string host, int port, TimeSpan timeout,
        CancellationToken token = default);
    Task<string> SendRequestAsync(string request, TimeSpan timeout,
        CancellationToken token = default);
    Task DisconnectAsync();
}
```

实现类必须保证：

- 连接、请求和断开支持超时或取消。
- 同一时间的请求与响应不会串线。
- 连接中断时触发`ConnectionLost`。
- 收到有效启动信号时触发`StartRequested`，并提供稳定周期号。
- `Dispose`能够停止后台任务并释放Socket或厂商SDK资源。
- 不在通信线程直接修改WPF界面。

## 真实Modbus适配建议

建议新增：

```text
Communication/ModbusTcpPlcClient.cs
```

它可以在内部将现有业务语义映射到线圈或保持寄存器：

| 业务语义 | 示例地址 | 方向 |
|---|---:|---|
| START | Coil 00001 | PLC → 上位机 |
| BUSY | Coil 00002 | 上位机 → PLC |
| COMPLETE | Coil 00003 | 上位机 → PLC |
| OK | Coil 00004 | 上位机 → PLC |
| NG | Coil 00005 | 上位机 → PLC |
| 周期号 | Holding Register 40001～40002 | 双向 |
| 心跳 | Holding Register 40003 | 双向 |

地址仅为示例，必须由实际PLC程序和电气设计确认。

## 接入前必须确认

- PLC品牌、型号和固件。
- Modbus TCP服务器IP和端口。
- Unit Identifier。
- 线圈和寄存器地址是否使用0基或1基。
- 16/32位整数的字节序和字序。
- START是电平还是上升沿触发。
- BUSY、COMPLETE、OK、NG的复位顺序。
- 通信超时后产线停机、报警或旁路策略。
- PLC重发相同周期号时的幂等行为。

## 测试要求

新适配器至少需要覆盖：

- 连接成功和连接超时。
- 正常心跳与心跳失败。
- 完整START/BUSY/RESULT握手。
- 重复周期号去重。
- 断线重连后不重复检测。
- 非法寄存器值和周期号处理。
- 500次连续握手。
- 真实PLC断网、重启和恢复测试。

未经真实PLC验证前，不应在README或简历中宣称已经支持某一PLC品牌或正式Modbus TCP产线。
