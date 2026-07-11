# DeskSnapshot

DeskSnapshot 是一个轻量、离线的 Windows 桌面图标布局备份工具，使用 WinUI 3 和 Windows App SDK 构建。

程序运行所需的 ICO 和 PNG 保存在 `src/DeskSnapshot/Assets/`。设计源图包 `desksnapshot_pack/` 仅在本地保留，不纳入 Git。

当前 Demo 已包含：

- 读取当前 Windows 桌面图标名称与坐标
- 将布局保存到本地 JSON 文件
- 以日期时间线浏览历史备份，区分手动、自动和恢复前备份及其产生原因
- 支持单选查看、恢复及多选批量删除备份
- 恢复布局前自动创建安全备份
- 保存显示器名称、设备标识及排列位置，恢复时可选择让图标跟随原显示器
- 显示分辨率、DPI、显示器数量和图标数量
- WinUI 原生可折叠导航栏
- 独立“关于”页面集中展示版本、本地数据位置和隐私说明
- 定时、程序启动、显示环境变化和图标布局变化自动备份
- 当前用户开机自启与系统托盘后台运行，支持托盘快速备份
- 简体中文、English 和跟随系统语言
- 便携版与单项目 MSIX 打包
- 深浅色主题与 Fluent/Mica 界面

## 开发环境

- Windows 10 1809 或更高版本
- .NET 10 SDK
- Windows App SDK 2.2
- x64 Windows 桌面环境

## 运行

```powershell
dotnet restore src/DeskSnapshot/DeskSnapshot.csproj --configfile NuGet.Config
dotnet run --project src/DeskSnapshot/DeskSnapshot.csproj -p:Platform=x64
```

首次还原需要联网下载 Windows App SDK NuGet 包。备份数据保存在：

```text
%LOCALAPPDATA%\DeskSnapshot\backups.json
```

路径不是写死的。便携版根据当前用户的 `LocalApplicationData` 动态生成；MSIX 版使用包专属的 `ApplicationData.LocalFolder`。应用“关于”页会显示当前实际路径。

## 发布与 MSIX

生成便携版和 MSIX：

```powershell
./scripts/publish.ps1 -Mode All -Version 1.0.0 -Clean -CreateTestCertificate
```

也可以用 `-Mode Folder` 或 `-Mode Msix` 单独生成。输出位于：

```text
artifacts/release/DeskSnapshot-<version>-win-x64/
```

测试证书仅用于本地安装和 CI 验证。正式发布时应使用与 `Package.appxmanifest` 中 Publisher 一致的代码签名证书，并通过 `-CertificatePath` 和 `-CertificatePassword` 传入。

当前 Win32 桌面读取、恢复、托盘和后台逻辑可在 MSIX 的 `runFullTrust` 进程中运行。开机自启会自动选择实现方式：便携版使用当前用户 Run 项，MSIX 版使用清单声明的 `StartupTask`。

## GitHub Actions

- `build.yml`：推送或 PR 时构建；推送到 `main` 后上传便携版和测试签名 MSIX。
- `release.yml`：手动输入版本和发布模式，生成可下载构建产物。

工作流结构参考 [FeranyDev/AutoLock](https://github.com/FeranyDev/AutoLock/tree/main/.github/workflows)。

若启动阶段出现异常，可查看诊断日志：

```text
%TEMP%\DeskSnapshot-startup.log
```

> 桌面图标读写依赖 Windows Explorer 的桌面 ListView。恢复过程中请勿重启 Explorer 或开启“自动排列图标”。
