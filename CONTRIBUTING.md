# 参与开发

感谢参与工业视觉上位机项目。提交修改前请遵循以下流程。

## 开发环境

- Windows 10/11 x64
- .NET 8 SDK
- Visual Studio 2022“.NET桌面开发”工作负载

## 修改流程

1. 从`main`创建功能分支，例如`feature/circularity-check`。
2. 每次提交只解决一个明确问题。
3. 新增业务行为时同步增加测试。
4. 运行：

```powershell
powershell -ExecutionPolicy Bypass -File scripts\test.ps1
```

5. 确认Release编译无错误，界面可启动。
6. Pull Request中说明修改原因、验证方式和界面变化。

## 提交信息建议

```text
feat: 增加中心偏移判定
fix: 修复重复PLC周期被执行两次
test: 增加旧数据库迁移测试
docs: 更新运行说明
```

## 项目边界

不要把当前文本TCP模拟协议描述为Modbus TCP，也不要在没有标定和实物验证时宣称毫米级精度或真实产线部署。
