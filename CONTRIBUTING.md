# Contributing to DeskSnapshot

感谢你愿意改进 DeskSnapshot。Bug 修复、文档、翻译、可访问性改进和功能建议都很欢迎。

## 开始之前

- 普通问题和功能建议请先搜索现有 Issues。
- 安全漏洞不要公开提交 Issue，请遵循 [SECURITY.md](SECURITY.md)。
- 参与项目即表示你同意遵守 [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md)。

## 开发环境

- Windows 10 1809 或更高版本
- .NET 10 SDK
- x64 Windows Explorer 桌面环境

```powershell
git clone https://github.com/FeranyDev/DeskSnapshot.git
cd DeskSnapshot
dotnet restore src/DeskSnapshot/DeskSnapshot.csproj --configfile NuGet.Config
dotnet build src/DeskSnapshot/DeskSnapshot.csproj -c Debug -p:Platform=x64
```

## 提交改动

1. 从 `main` 创建主题分支，例如 `fix/preview-layout` 或 `feat/import-backup`。
2. 保持改动聚焦，并同步更新中英文资源和相关文档。
3. 至少验证 Debug 和 Release x64 构建。
4. 不要提交本地备份、签名证书、构建产物、缓存或设计源文件。
5. 提交 Pull Request，说明行为变化、验证方式和相关 Issue；界面变更建议附截图。

建议使用简洁的 Conventional Commits 风格，例如：

```text
fix: prevent automatic backup after restore
feat: add monitor-aware layout restore
docs: clarify MSIX installation
```

## 代码约定

- C# 启用 nullable 和隐式 using，优先保持现有项目风格。
- UI 文案不得直接只写一种语言；同步维护 `zh-CN` 与 `en-US` 资源。
- 桌面读写涉及 Explorer 进程边界，必须保留超时、错误处理和安全回退。
- 不得在日志、示例或测试中提交个人桌面图标名称和本地路径。
