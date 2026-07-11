# Security Policy

## Supported versions

DeskSnapshot is currently in active development. Security fixes are applied to the latest release and the `main` branch; older builds may not receive patches.

## Reporting a vulnerability

请不要通过公开 Issue 报告安全漏洞，也不要附上包含个人桌面图标、用户名或本地路径的日志。

Preferred method:

1. Open the repository's **Security** tab.
2. Choose **Report a vulnerability** to create a private security advisory.
3. Include affected versions, reproduction steps, impact, and a minimal proof of concept when possible.

If private vulnerability reporting is unavailable, contact the repository owner through the [FeranyDev GitHub profile](https://github.com/FeranyDev) and request a private channel before sharing details.

You should receive an acknowledgement within 7 days. Please allow time for investigation and a coordinated fix before public disclosure.

## Scope notes

DeskSnapshot reads and writes the Windows Explorer desktop ListView and stores layout data locally. Reports involving unexpected privilege escalation, unsafe file writes, package signing, startup persistence, or unintended disclosure of desktop data are especially helpful.
