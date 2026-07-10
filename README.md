# DeskSnapshot

DeskSnapshot 是一个使用 WinUI 3 构建的 Windows 桌面图标位置备份工具。

应用图标的 SVG、ICO 和多尺寸 PNG 源文件保存在 `desksnapshot_pack/`。

当前 Demo 已包含：

- 读取当前 Windows 桌面图标名称与坐标
- 将布局保存到本地 JSON 文件
- 浏览和删除历史备份
- 恢复布局前自动创建安全备份
- 显示分辨率、DPI、显示器数量和图标数量
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

若启动阶段出现异常，可查看诊断日志：

```text
%TEMP%\DeskSnapshot-startup.log
```

> 桌面图标读写依赖 Windows Explorer 的桌面 ListView。恢复过程中请勿重启 Explorer 或开启“自动排列图标”。
