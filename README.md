# DeskSnapshot

[![Build](https://github.com/FeranyDev/DeskSnapshot/actions/workflows/build.yml/badge.svg)](https://github.com/FeranyDev/DeskSnapshot/actions/workflows/build.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Windows](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4.svg)](https://www.microsoft.com/windows)

DeskSnapshot 是一个轻量、离线的 Windows 桌面图标布局备份工具，使用 WinUI 3 和 Windows App SDK 构建。

> English: DeskSnapshot is a lightweight, offline WinUI 3 utility for backing up, previewing, and restoring Windows desktop icon layouts.

项目目前处于首个公开版本的积极开发阶段。建议在重要桌面环境中使用前自行验证备份和恢复结果。

程序运行所需的 ICO 和 PNG 保存在 `src/DeskSnapshot/Assets/`。设计源图包 `desksnapshot_pack/` 仅在本地保留，不纳入 Git。

当前 Demo 已包含：

- 读取当前 Windows 桌面图标名称与坐标
- 将布局保存到本地 JSON 文件
- 以日期时间线浏览历史备份，区分手动、自动和恢复前备份及其产生原因
- 支持单选查看、恢复及多选批量删除备份
- 在布局预览中对比当前桌面，标记移动、新增、缺失和未变化图标
- 恢复布局前自动创建安全备份
- 保存显示器名称、设备标识及排列位置，恢复时可选择让图标跟随原显示器
- 将常用备份保存为办公、游戏或单屏等显示器布局档案；相同组合出现时仅提示恢复
- 原显示器缺失时可在恢复确认中手动映射目标显示器，并记住相同显示器组合的选择
- 显示分辨率、DPI、显示器数量和图标数量
- WinUI 原生可折叠导航栏
- 独立“关于”页面集中展示版本、本地数据位置和隐私说明
- 定时、程序启动、显示环境变化和图标布局变化自动备份
- 当前用户开机自启与系统托盘后台运行，支持托盘快速备份
- 简体中文、English 和跟随系统语言
- 便携版与单项目 MSIX 打包
- 深浅色主题与 Fluent/Mica 界面

## 获取与安装

正式构建会发布在 [GitHub Releases](https://github.com/FeranyDev/DeskSnapshot/releases)。

- **Portable**：解压后直接运行 `DeskSnapshot.exe`，无需管理员权限。
- **MSIX**：适合包管理和系统集成，但安装包必须由本机信任的证书签名。
- **Actions artifacts**：用于开发测试，测试签名 MSIX 不应被视为正式发行签名。

DeskSnapshot 不需要管理员权限。开机自启只写入当前用户范围，备份和设置默认保存在当前用户的本地应用数据目录。

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

## 测试

核心逻辑使用 MSTest 和 Microsoft.Testing.Platform，测试不读写真实桌面或正式备份目录：

```powershell
dotnet test tests/DeskSnapshot.Tests/DeskSnapshot.Tests.csproj -c Release
```

当前覆盖桌面差异匹配、重复名称和显示环境判断、25,000 图标性能场景、备份父子关系、自动备份保留策略，以及 JSON 往返、覆盖写入和损坏文件隔离。

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

## 隐私与数据

- 布局、设置和日志仅保存在本机，应用不包含云同步或遥测上传。
- 备份可能包含桌面图标显示名称、显示器型号和坐标。
- 提交 Issue 前请从日志、截图和备份 JSON 中移除个人路径与私人图标名称。

## 已知限制

- 桌面图标读写依赖 Windows Explorer 的桌面 ListView，不支持第三方桌面外壳。
- 恢复过程中请勿重启 Explorer 或开启 Windows“自动排列图标”。
- 同名图标按其在 Explorer 中的出现顺序匹配。
- 原显示器缺失时，显示器跟随恢复会安全回退到备份中的绝对坐标。
- 当前仅构建和测试 x64 版本。

## 参与和支持

- 提交改动前请阅读 [CONTRIBUTING.md](CONTRIBUTING.md) 和 [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md)。
- 后续功能与验收清单见 [ROADMAP.md](ROADMAP.md)。
- 使用问题与诊断信息见 [SUPPORT.md](SUPPORT.md)。
- 安全漏洞请按 [SECURITY.md](SECURITY.md) 私下报告。
- 版本变化记录在 [CHANGELOG.md](CHANGELOG.md)。

## 许可证

DeskSnapshot 使用 [MIT License](LICENSE) 开源。
