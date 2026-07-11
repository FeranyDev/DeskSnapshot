using Microsoft.Win32;
using Windows.ApplicationModel;

namespace DeskSnapshot.Services;

public sealed class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DeskSnapshot";

    private const string StartupTaskId = "DeskSnapshotStartup";

    public async Task<bool> GetIsEnabledAsync()
    {
        if (AppDataPathService.IsPackaged())
        {
            var task = await StartupTask.GetAsync(StartupTaskId);
            return task.State == StartupTaskState.Enabled;
        }

        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
        return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
    }

    public async Task SetEnabledAsync(bool enabled)
    {
        if (AppDataPathService.IsPackaged())
        {
            var task = await StartupTask.GetAsync(StartupTaskId);
            if (!enabled)
            {
                task.Disable();
                return;
            }

            var state = await task.RequestEnableAsync();
            if (state != StartupTaskState.Enabled)
            {
                throw new InvalidOperationException(LocalizationService.Get("StartupRequestDenied"));
            }
            return;
        }

        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, true)
            ?? throw new InvalidOperationException(LocalizationService.Get("StartupRegistryUnavailable"));

        if (!enabled)
        {
            key.DeleteValue(ValueName, false);
            return;
        }

        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new InvalidOperationException(LocalizationService.Get("ExecutablePathUnavailable"));
        }

        key.SetValue(ValueName, $"\"{executablePath}\"");
    }

}
