using System.Collections.ObjectModel;
using System.Text;
using DeskSnapshot.Models;
using DeskSnapshot.Services;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace DeskSnapshot;

public sealed partial class MainWindow : Window
{
    private readonly BackupStore _store = new();
    private readonly SettingsStore _settingsStore = new();
    private readonly DesktopIconLayoutService _layoutService = new();
    private readonly List<DesktopLayoutBackup> _backups = [];
    private readonly List<BackupPreviewWindow> _previewWindows = [];
    private readonly DispatcherTimer _scheduledBackupTimer = new();
    private readonly DispatcherTimer _eventWatchTimer = new();
    private AppSettings _settings = new();
    private bool _settingsLoaded;
    private bool _autoBackupBusy;
    private string? _lastDesktopFingerprint;
    private string? _lastDisplayFingerprint;
    private string? _pendingDesktopFingerprint;
    private int _pendingDesktopStability;

    public ObservableCollection<BackupListItem> BackupItems { get; } = [];

    public MainWindow()
    {
        App.Log("MainWindow: InitializeComponent begin");
        InitializeComponent();
        App.Log("MainWindow: InitializeComponent complete");
        Title = "DeskSnapshot - 桌面布局备份";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        App.Log("MainWindow: title bar complete");

        try
        {
            SystemBackdrop = new MicaBackdrop();
            App.Log("MainWindow: Mica complete");
        }
        catch (Exception exception)
        {
            // Mica 在部分旧系统上不可用，使用默认背景即可。
            App.Log($"MainWindow: Mica unavailable: {exception.Message}");
        }

        ResizeWindow();
        App.Log("MainWindow: resize complete");
        StoragePathText.Text = _store.FilePath;
        _scheduledBackupTimer.Tick += ScheduledBackupTimer_Tick;
        _eventWatchTimer.Tick += EventWatchTimer_Tick;
        Closed += MainWindow_Closed;
        RootGrid.Loaded += MainWindow_Loaded;
        AppNavigationView.SelectedItem = DashboardNavItem;
        ShowSection(DashboardSection);
        App.Log("MainWindow: constructor complete");
    }

    private void ResizeWindow()
    {
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(handle);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        App.ApplyWindowIcon(appWindow);
        appWindow.Resize(new SizeInt32(1180, 760));
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _backups.AddRange(await _store.LoadAsync());
            _settings = await _settingsStore.LoadAsync();
            RefreshBackupItems();
            RefreshEnvironmentSummary();
            ApplySettingsToUi();
            _settingsLoaded = true;
            await InitializeAutoBackupAsync();
            ConfigureAutoBackupTimers();
        }
        catch (Exception exception)
        {
            ShowNotice("读取失败", exception.Message, InfoBarSeverity.Error);
        }
    }

    private void RefreshEnvironmentSummary()
    {
        var environment = _layoutService.ReadEnvironment();
        HeroEnvironmentText.Text = $"{environment.VirtualWidth} × {environment.VirtualHeight}  ·  {environment.MonitorCount} 台显示器";
        HeroDpiText.Text = $"DPI {environment.Dpi}  ·  {environment.Dpi / 96d:P0}";
        MonitorCountText.Text = environment.MonitorCount.ToString();
        App.Log($"Display environment: {environment.VirtualWidth}x{environment.VirtualHeight}, DPI {environment.Dpi}, monitors {environment.MonitorCount}");
    }

    private void RefreshBackupItems()
    {
        BackupItems.Clear();
        foreach (var backup in _backups.OrderByDescending(item => item.CreatedAt))
        {
            BackupItems.Add(new BackupListItem { Backup = backup });
        }

        BackupCountText.Text = _backups.Count.ToString();
        EmptyBackupsText.Visibility = _backups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        BackupsList.Visibility = _backups.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        var latest = _backups.OrderByDescending(item => item.CreatedAt).FirstOrDefault();
        LastBackupText.Text = latest is null ? "尚未备份" : latest.CreatedAt.LocalDateTime.ToString("MM-dd  HH:mm");
        HeroIconCountText.Text = latest is null ? "等待首次备份" : $"最近记录 {latest.Icons.Count} 个图标";
    }

    private async void CreateBackup_Click(object sender, RoutedEventArgs e)
    {
        var nameBox = new TextBox
        {
            Header = "备份名称",
            Text = $"桌面布局 {DateTime.Now:MM-dd HH:mm}",
            SelectionStart = 0,
            SelectionLength = $"桌面布局 {DateTime.Now:MM-dd HH:mm}".Length
        };
        var noteBox = new TextBox
        {
            Header = "备注（可选）",
            PlaceholderText = "例如：连接双显示器后的布局"
        };
        var panel = new StackPanel { Spacing = 14 };
        panel.Children.Add(nameBox);
        panel.Children.Add(noteBox);

        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = "保存当前桌面布局",
            Content = panel,
            PrimaryButtonText = "创建备份",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var backupName = string.IsNullOrWhiteSpace(nameBox.Text)
            ? $"桌面布局 {DateTime.Now:yyyy-MM-dd HH:mm}"
            : nameBox.Text.Trim();
        var backupNote = noteBox.Text.Trim();

        SetBusy(true, "正在读取桌面图标位置…");
        try
        {
            var backup = await Task.Run(() => _layoutService.Capture(backupName, backupNote));
            App.Log($"Create backup: captured {backup.Icons.Count} icons");
            _backups.Add(backup);
            await _store.SaveAsync(_backups);
            App.Log($"Create backup: saved {backup.Id} to {_store.FilePath}");
            RefreshBackupItems();
            ShowNotice("备份已完成", $"已记录 {backup.Icons.Count} 个桌面图标。", InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            App.Log($"Create backup failed: {exception}");
            ShowNotice("备份失败", exception.Message, InfoBarSeverity.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void RestoreBackup_Click(object sender, RoutedEventArgs e)
    {
        if (BackupsList.SelectedItem is not BackupListItem selected)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = "恢复桌面布局？",
            Content = $"将桌面图标恢复到“{selected.Name}”的位置。当前布局会先保存为安全备份。",
            PrimaryButtonText = "恢复",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        SetBusy(true, "正在创建安全备份…");
        try
        {
            var safetyName = $"恢复前安全备份 {DateTime.Now:MM-dd HH:mm}";
            var safetyBackup = await Task.Run(() => _layoutService.Capture(safetyName, $"恢复“{selected.Name}”前自动创建", true));
            _backups.Add(safetyBackup);
            await _store.SaveAsync(_backups);

            BusyText.Text = "正在恢复图标位置…";
            var result = await Task.Run(() => _layoutService.Restore(selected.Backup));
            RefreshBackupItems();

            var severity = result.Failed == 0 ? InfoBarSeverity.Success : InfoBarSeverity.Warning;
            var message = $"已恢复 {result.Restored} 个图标";
            if (result.Missing > 0) message += $"，跳过 {result.Missing} 个缺失图标";
            if (result.Failed > 0) message += $"，{result.Failed} 个图标恢复失败";
            ShowNotice("恢复完成", message + "。", severity);
        }
        catch (Exception exception)
        {
            ShowNotice("恢复失败", exception.Message, InfoBarSeverity.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void DeleteBackup_Click(object sender, RoutedEventArgs e)
    {
        if (BackupsList.SelectedItem is not BackupListItem selected)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = "删除这条备份？",
            Content = $"“{selected.Name}”将从本机永久删除。",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        _backups.RemoveAll(item => item.Id == selected.Backup.Id);
        await _store.SaveAsync(_backups);
        RefreshBackupItems();
        ShowNotice("备份已删除", "本地记录已更新。", InfoBarSeverity.Success);
    }

    private void BackupsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selected = BackupsList.SelectedItem as BackupListItem;
        RestoreBackupButton.IsEnabled = selected is not null;
        DeleteBackupButton.IsEnabled = selected is not null;
        ViewBackupButton.IsEnabled = selected is not null;
        SelectionHintText.Text = selected is null
            ? "选择一条记录以执行操作"
            : $"已选择：{selected.Name}";
    }

    private void ViewBackup_Click(object sender, RoutedEventArgs e) => OpenSelectedBackupPreview();

    private void BackupsList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => OpenSelectedBackupPreview();

    private void OpenSelectedBackupPreview()
    {
        if (BackupsList.SelectedItem is not BackupListItem selected)
        {
            return;
        }

        var previewWindow = new BackupPreviewWindow(selected.Backup, this);
        _previewWindows.Add(previewWindow);
        previewWindow.Closed += (_, _) => _previewWindows.Remove(previewWindow);
        previewWindow.Activate();
    }

    private void AppNavigationView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var tag = args.SelectedItemContainer?.Tag?.ToString();
        ShowSection(tag switch
        {
            "backups" => BackupsSection,
            "settings" => SettingsSection,
            _ => DashboardSection
        });
    }

    private void AppNavigationView_PaneOpened(NavigationView sender, object args) =>
        LocalStorageFooter.Visibility = Visibility.Visible;

    private void AppNavigationView_PaneClosed(NavigationView sender, object args) =>
        LocalStorageFooter.Visibility = Visibility.Collapsed;

    private void BackupsNav_Click(object sender, RoutedEventArgs e)
    {
        AppNavigationView.SelectedItem = BackupsNavItem;
        ShowSection(BackupsSection);
    }

    private void ShowSection(FrameworkElement section)
    {
        DashboardSection.Visibility = section == DashboardSection ? Visibility.Visible : Visibility.Collapsed;
        BackupsSection.Visibility = section == BackupsSection ? Visibility.Visible : Visibility.Collapsed;
        SettingsSection.Visibility = section == SettingsSection ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplySettingsToUi()
    {
        AutoBackupToggle.IsOn = _settings.AutoBackupEnabled;
        ScheduledBackupToggle.IsOn = _settings.ScheduledBackupEnabled;
        StartupTriggerCheckBox.IsChecked = _settings.BackupOnStartup;
        DisplayTriggerCheckBox.IsChecked = _settings.BackupOnDisplayChange;
        DesktopTriggerCheckBox.IsChecked = _settings.BackupOnDesktopChange;

        foreach (var item in BackupIntervalComboBox.Items.OfType<ComboBoxItem>())
        {
            if (int.TryParse(item.Tag?.ToString(), out var minutes) && minutes == _settings.BackupIntervalMinutes)
            {
                BackupIntervalComboBox.SelectedItem = item;
                break;
            }
        }

        UpdateAutoBackupControls();
    }

    private async void AutoBackupSettings_Changed(object sender, RoutedEventArgs e)
    {
        if (!_settingsLoaded)
        {
            return;
        }

        UpdateSettingsFromUi();
        await _settingsStore.SaveAsync(_settings);
        UpdateAutoBackupControls();
        ConfigureAutoBackupTimers();

        if (_settings.AutoBackupEnabled && _lastDesktopFingerprint is null)
        {
            await EstablishAutoBackupBaselineAsync();
        }
    }

    private async void BackupIntervalComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_settingsLoaded)
        {
            return;
        }

        UpdateSettingsFromUi();
        await _settingsStore.SaveAsync(_settings);
        UpdateAutoBackupControls();
        ConfigureAutoBackupTimers();
    }

    private void UpdateSettingsFromUi()
    {
        _settings.AutoBackupEnabled = AutoBackupToggle.IsOn;
        _settings.ScheduledBackupEnabled = ScheduledBackupToggle.IsOn;
        _settings.BackupOnStartup = StartupTriggerCheckBox.IsChecked == true;
        _settings.BackupOnDisplayChange = DisplayTriggerCheckBox.IsChecked == true;
        _settings.BackupOnDesktopChange = DesktopTriggerCheckBox.IsChecked == true;

        if (BackupIntervalComboBox.SelectedItem is ComboBoxItem selected &&
            int.TryParse(selected.Tag?.ToString(), out var minutes))
        {
            _settings.BackupIntervalMinutes = minutes;
        }
    }

    private void UpdateAutoBackupControls()
    {
        var enabled = _settings.AutoBackupEnabled;
        ScheduledBackupToggle.IsEnabled = enabled;
        BackupIntervalComboBox.IsEnabled = enabled && _settings.ScheduledBackupEnabled;
        StartupTriggerCheckBox.IsEnabled = enabled;
        DisplayTriggerCheckBox.IsEnabled = enabled;
        DesktopTriggerCheckBox.IsEnabled = enabled;

        if (!enabled)
        {
            AutoBackupStatusText.Text = "自动备份已关闭";
            return;
        }

        var modes = new List<string>();
        if (_settings.ScheduledBackupEnabled)
        {
            modes.Add($"每 {_settings.BackupIntervalMinutes} 分钟");
        }
        if (_settings.BackupOnStartup) modes.Add("启动时");
        if (_settings.BackupOnDisplayChange) modes.Add("显示环境变化");
        if (_settings.BackupOnDesktopChange) modes.Add("图标布局变化");
        AutoBackupStatusText.Text = modes.Count == 0
            ? "自动备份已开启，但尚未选择触发方式"
            : $"已启用：{string.Join("、", modes)}";
    }

    private async Task InitializeAutoBackupAsync()
    {
        if (!_settings.AutoBackupEnabled)
        {
            return;
        }

        var snapshot = await CaptureAutomaticSnapshotAsync();
        if (snapshot is null)
        {
            return;
        }

        SetAutoBackupBaseline(snapshot);
        if (_settings.BackupOnStartup)
        {
            await CreateAutomaticBackupAsync("程序启动", snapshot);
        }
    }

    private async Task EstablishAutoBackupBaselineAsync()
    {
        var snapshot = await CaptureAutomaticSnapshotAsync();
        if (snapshot is not null)
        {
            SetAutoBackupBaseline(snapshot);
        }
    }

    private void ConfigureAutoBackupTimers()
    {
        _scheduledBackupTimer.Stop();
        _eventWatchTimer.Stop();

        if (!_settings.AutoBackupEnabled)
        {
            return;
        }

        if (_settings.ScheduledBackupEnabled)
        {
            _scheduledBackupTimer.Interval = TimeSpan.FromMinutes(Math.Clamp(_settings.BackupIntervalMinutes, 1, 1440));
            _scheduledBackupTimer.Start();
        }

        if (_settings.BackupOnDisplayChange || _settings.BackupOnDesktopChange)
        {
            _eventWatchTimer.Interval = TimeSpan.FromSeconds(10);
            _eventWatchTimer.Start();
        }
    }

    private async void ScheduledBackupTimer_Tick(object? sender, object e)
    {
        await CreateAutomaticBackupAsync("定时");
    }

    private async void EventWatchTimer_Tick(object? sender, object e)
    {
        if (_autoBackupBusy || !_settings.AutoBackupEnabled)
        {
            return;
        }

        _autoBackupBusy = true;
        try
        {
            var snapshot = await CaptureAutomaticSnapshotAsync();
            if (snapshot is null)
            {
                return;
            }

            var displayFingerprint = GetDisplayFingerprint(snapshot.Environment);
            var desktopFingerprint = GetDesktopFingerprint(snapshot);
            if (_lastDisplayFingerprint is null || _lastDesktopFingerprint is null)
            {
                SetAutoBackupBaseline(snapshot);
                return;
            }

            if (_settings.BackupOnDisplayChange && displayFingerprint != _lastDisplayFingerprint)
            {
                await SaveAutomaticBackupCoreAsync("显示环境变化", snapshot);
                return;
            }

            if (_settings.BackupOnDesktopChange && desktopFingerprint != _lastDesktopFingerprint)
            {
                if (_pendingDesktopFingerprint == desktopFingerprint)
                {
                    _pendingDesktopStability++;
                }
                else
                {
                    _pendingDesktopFingerprint = desktopFingerprint;
                    _pendingDesktopStability = 1;
                }

                if (_pendingDesktopStability >= 2)
                {
                    await SaveAutomaticBackupCoreAsync("图标布局变化", snapshot);
                }
                return;
            }

            _pendingDesktopFingerprint = null;
            _pendingDesktopStability = 0;
            _lastDisplayFingerprint = displayFingerprint;
            if (!_settings.BackupOnDesktopChange)
            {
                _lastDesktopFingerprint = desktopFingerprint;
            }
        }
        finally
        {
            _autoBackupBusy = false;
        }
    }

    private async Task CreateAutomaticBackupAsync(string reason, DesktopLayoutBackup? snapshot = null)
    {
        if (_autoBackupBusy || !_settings.AutoBackupEnabled)
        {
            return;
        }

        _autoBackupBusy = true;
        try
        {
            snapshot ??= await CaptureAutomaticSnapshotAsync();
            if (snapshot is not null)
            {
                await SaveAutomaticBackupCoreAsync(reason, snapshot);
            }
        }
        finally
        {
            _autoBackupBusy = false;
        }
    }

    private async Task<DesktopLayoutBackup?> CaptureAutomaticSnapshotAsync()
    {
        try
        {
            return await Task.Run(() => _layoutService.Capture("自动检测"));
        }
        catch (Exception exception)
        {
            App.Log($"Automatic backup capture failed: {exception}");
            AutoBackupStatusText.Text = $"自动备份检测失败：{exception.Message}";
            return null;
        }
    }

    private async Task SaveAutomaticBackupCoreAsync(string reason, DesktopLayoutBackup snapshot)
    {
        snapshot.Name = $"自动备份 · {reason} {DateTime.Now:MM-dd HH:mm}";
        snapshot.Note = $"由“{reason}”触发";
        snapshot.CreatedAt = DateTimeOffset.Now;
        snapshot.IsAutomaticBackup = true;
        snapshot.IsSafetyBackup = false;
        _backups.Add(snapshot);

        var retention = Math.Clamp(_settings.AutomaticBackupRetention, 1, 200);
        var expired = _backups
            .Where(item => item.IsAutomaticBackup)
            .OrderByDescending(item => item.CreatedAt)
            .Skip(retention)
            .ToList();
        foreach (var backup in expired)
        {
            _backups.Remove(backup);
        }

        await _store.SaveAsync(_backups);
        SetAutoBackupBaseline(snapshot);
        RefreshBackupItems();
        AutoBackupStatusText.Text = $"上次自动备份：{DateTime.Now:HH:mm:ss}（{reason}）";
        App.Log($"Automatic backup saved: {snapshot.Id}, reason={reason}, icons={snapshot.Icons.Count}");
    }

    private void SetAutoBackupBaseline(DesktopLayoutBackup snapshot)
    {
        _lastDisplayFingerprint = GetDisplayFingerprint(snapshot.Environment);
        _lastDesktopFingerprint = GetDesktopFingerprint(snapshot);
        _pendingDesktopFingerprint = null;
        _pendingDesktopStability = 0;
    }

    private static string GetDisplayFingerprint(DesktopEnvironment environment) =>
        $"{environment.VirtualLeft},{environment.VirtualTop},{environment.VirtualWidth},{environment.VirtualHeight},{environment.Dpi},{environment.MonitorCount}";

    private static string GetDesktopFingerprint(DesktopLayoutBackup snapshot)
    {
        var builder = new StringBuilder(snapshot.Icons.Count * 32);
        foreach (var icon in snapshot.Icons.OrderBy(item => item.CaptureOrder))
        {
            builder.Append(icon.Name).Append('\u001f').Append(icon.X).Append(',').Append(icon.Y).Append('\u001e');
        }
        return builder.ToString();
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        _scheduledBackupTimer.Stop();
        _eventWatchTimer.Stop();
    }

    private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RootGrid is null || ThemeComboBox.SelectedItem is not ComboBoxItem selected)
        {
            return;
        }

        RootGrid.RequestedTheme = selected.Tag?.ToString() switch
        {
            "Light" => ElementTheme.Light,
            "Dark" => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
    }

    private void SetBusy(bool isBusy, string message = "正在处理桌面布局…")
    {
        BusyText.Text = message;
        BusyOverlay.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowNotice(string title, string message, InfoBarSeverity severity)
    {
        ActionInfoBar.Title = title;
        ActionInfoBar.Message = message;
        ActionInfoBar.Severity = severity;
        ActionInfoBar.IsOpen = true;
    }
}
