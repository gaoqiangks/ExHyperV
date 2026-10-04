# 自动连接、窗口状态与无密码登录

在 ExHyperV 的设置页点击“虚拟机自动连接与无密码登录设置”。也可以运行 `ExHyperV.exe --settings`，即使已开启自动连接，也能打开设置。

可设置：

- 是否在启动程序时自动连接。
- 目标虚拟机名称。
- 控制台状态：普通窗口、最大化窗口、最小化或全屏。
- 连接成功后是否关闭管理主窗口。
- 连接后是否自动登录 / 解锁当前选中的无密码 Windows 账户。

保存后在下次启动或连接时生效。默认不开启自动连接或无密码登录。配置保存在 `%LOCALAPPDATA%\ExHyperV\AutoConnect.json`，日志保存在同目录的 `logs\autoconnect.log`。

## 命令行

- `ExHyperV.exe`：使用保存的设置。
- `ExHyperV.exe --auto-connect "VM NAME"`：本次连接指定虚拟机，窗口状态沿用设置。
- `ExHyperV.exe --no-auto-connect`：本次只打开管理界面。
- `ExHyperV.exe --settings`：打开设置，不进行自动连接。

启动后通过 Hyper-V WMI 按名称等待虚拟机运行，关机或保存状态的虚拟机使用正常启动操作。优先等增强会话就绪；运行 60 秒后允许普通控制台回退。等待最多 5 分钟，不强制重启或关机。

## 无密码登录 / 解锁

RDP 连接后延迟 2.5 秒，通过 `IMsRdpClientNonScriptable.SendKeys` 向当前来宾会话发送 Enter，最多两次。收到 `OnLoginComplete` 后立即停止。断开连接或关闭窗口也会取消尚未发送的按键。

这项功能用于连接或重连时显示的无密码登录 / 解锁界面。有密码、账户选择、登录声明等额外步骤仍需人工处理。不会更改来宾密码、注册表、安全策略，也不会尝试猜测密码。登录后在会话内再次锁屏不会持续自动重试；重连可再次触发。

参考：[Microsoft SendKeys API](https://learn.microsoft.com/windows/win32/termserv/imsrdpclientnonscriptable-sendkeys)、[OnLoginComplete](https://learn.microsoft.com/windows/win32/termserv/imstscaxevents-onlogincomplete)。

## 构建与发布

需要 Windows、Visual Studio 的 .NET 桌面开发工具和 Hyper-V / RDP COM 类型库。使用 Visual Studio MSBuild（COM 引用解析需要完整 MSBuild）：

```powershell
MSBuild.exe src\ExHyperV.csproj /restore /t:Publish /p:Configuration=Release /p:RuntimeIdentifier=win-x64 /p:SelfContained=true /p:PublishSingleFile=false /p:DebugType=none /p:DebugSymbols=false /p:PublishDir=publish\
```

部署整个发布文件夹，包括原生运行库；仅复制 exe 可能导致启动失败。自动连接仍需 Hyper-V 管理权限；本项目的管理员运行要求沿用原配置。

## 验证

在本机 Windows 11 增强会话中验证了四种窗口状态、连接后关闭主窗口、无密码登录以及断开后重连解锁。自动登录成功以 RDP 登录完成事件和实际桌面画面确认。没有重启宿主机或改变虚拟机电源 / GPU 设置。
目标虚拟机已有自动连接实例时，再次启动 ExHyperV 会打开管理主界面，不会关闭新实例或重复连接。

设置中可关闭已有连接时打开管理主界面的行为。默认每次连接优先使用增强会话；关闭该选项后沿用手动选择的会话模式。增强会话不可用或连接失败时仍可回退基本会话。
主界面默认进入虚拟机管理页面，可在设置中关闭该开关恢复首页。
