using System.Collections.ObjectModel;
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
    private readonly DesktopIconLayoutService _layoutService = new();
    private readonly List<DesktopLayoutBackup> _backups = [];
    private readonly List<BackupPreviewWindow> _previewWindows = [];

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
        RootGrid.Loaded += MainWindow_Loaded;
        ShowSection(DashboardSection);
        App.Log("MainWindow: constructor complete");
    }

    private void ResizeWindow()
    {
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(handle);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.Resize(new SizeInt32(1180, 760));
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _backups.AddRange(await _store.LoadAsync());
            RefreshBackupItems();
            RefreshEnvironmentSummary();
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

    private void DashboardNav_Click(object sender, RoutedEventArgs e) => ShowSection(DashboardSection);

    private void BackupsNav_Click(object sender, RoutedEventArgs e) => ShowSection(BackupsSection);

    private void SettingsNav_Click(object sender, RoutedEventArgs e) => ShowSection(SettingsSection);

    private void ShowSection(FrameworkElement section)
    {
        DashboardSection.Visibility = section == DashboardSection ? Visibility.Visible : Visibility.Collapsed;
        BackupsSection.Visibility = section == BackupsSection ? Visibility.Visible : Visibility.Collapsed;
        SettingsSection.Visibility = section == SettingsSection ? Visibility.Visible : Visibility.Collapsed;

        DashboardNavButton.Background = section == DashboardSection ? (Brush)Application.Current.Resources["SubtleBrush"] : new SolidColorBrush(Colors.Transparent);
        BackupsNavButton.Background = section == BackupsSection ? (Brush)Application.Current.Resources["SubtleBrush"] : new SolidColorBrush(Colors.Transparent);
        SettingsNavButton.Background = section == SettingsSection ? (Brush)Application.Current.Resources["SubtleBrush"] : new SolidColorBrush(Colors.Transparent);
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
