# Support

## 使用问题

请先查看 [README](README.md) 中的系统要求、运行方式和已知限制，然后搜索已有 Issues。

如果仍无法解决，请提交 Bug Report，并尽量提供：

- Windows 版本、显示器数量和排列方式
- DeskSnapshot 版本及便携版/MSIX 安装方式
- Release 文件的 SHA-256 校验结果，以及“数字签名”中显示的证书指纹（指纹和公开 CER 可以提供）
- 可重复的操作步骤、预期结果和实际结果
- `%TEMP%\DeskSnapshot-startup.log` 中与问题相关的内容

提交日志或截图前，请移除用户名、本地路径和私人桌面图标名称。

如果 Windows 提示证书不受信任，请确认 CER、MSIX 和 `SHA256SUMS.txt` 来自同一个 GitHub Release。不要通过 Issue、邮件或聊天发送 PFX、证书密码、Base64 私钥内容；官方 Release 也不会要求这些信息。

## 功能建议

使用 Feature Request 模板描述使用场景和预期行为。项目不保证实现时间，但会保留有价值的讨论。

## 安全问题

安全漏洞请按 [SECURITY.md](SECURITY.md) 私下报告，不要公开提交 Issue。
