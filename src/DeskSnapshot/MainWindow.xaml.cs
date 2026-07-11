using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Text;
using DeskSnapshot.Models;
using DeskSnapshot.Services;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace DeskSnapshot;

public sealed partial class MainWindow : Window
{
    private readonly BackupStore _store = new();
    private readonly SettingsStore _settingsStore = new();
    private readonly StartupService _startupService = new();
    private readonly DesktopIconLayoutService _layoutService = new();
    private readonly List<DesktopLayoutBackup> _backups = [];
    private readonly List<BackupPreviewWindow> _previewWindows = [];
    private readonly DispatcherTimer _scheduledBackupTimer = new();
    private readonly DispatcherTimer _eventWatchTimer = new();
    private AppSettings _settings = new();
    private AppWindow? _appWindow;
    private TrayIconService? _trayIconService;
    private IntPtr _windowHandle;
    private bool _settingsLoaded;
    private bool _autoBackupBusy;
    private bool _trayBackupBusy;
    private bool _automaticBackupsSuspended;
    private bool _syncingBackupSelection;
    private bool _isExiting;
    private string? _lastDesktopFingerprint;
    private string? _lastDisplayFingerprint;
    private string? _pendingDesktopFingerprint;
    private int _pendingDesktopStability;

    public ObservableCollection<BackupTimelineGroup> BackupTimelineGroups { get; } = [];
    public CollectionViewSource BackupTimelineSource { get; } = new() { IsSourceGrouped = true };

    public MainWindow()
    {
        App.Log("MainWindow: InitializeComponent begin");
        InitializeComponent();
        LocalizationBindings.Apply(RootGrid);
        BackupTimelineSource.Source = BackupTimelineGroups;
        App.Log("MainWindow: InitializeComponent complete");
        Title = LocalizationService.Get("WindowTitle");
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
        InitializeTrayIcon();
        StoragePathText.Text = _store.FilePath;
        var version = typeof(MainWindow).Assembly.GetName().Version;
        AppVersionText.Text = version is null
            ? LocalizationService.Format("VersionFormat", 1, 0, 0)
            : LocalizationService.Format("VersionFormat", version.Major, version.Minor, version.Build);
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
        _windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(_windowHandle);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        App.ApplyWindowIcon(_appWindow);
        _appWindow.Resize(new SizeInt32(1350, 950));
        _appWindow.Closing += AppWindow_Closing;
    }

    private void InitializeTrayIcon()
    {
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "DeskSnapshot.ico");
            _trayIconService = new TrayIconService(_windowHandle, iconPath);
            _trayIconService.OpenRequested += ShowFromTray;
            _trayIconService.BackupRequested += TrayIcon_BackupRequested;
            _trayIconService.ExitRequested += ExitFromTray;
        }
        catch (Exception exception)
        {
            App.Log($"Tray icon unavailable: {exception}");
        }
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            LocalizationBindings.Apply(RootGrid);
            _backups.AddRange(await _store.LoadAsync());
            _settings = await _settingsStore.LoadAsync();
            _settings.RunAtStartup = await _startupService.GetIsEnabledAsync();
            RefreshBackupItems();
            RefreshEnvironmentSummary();
            ApplySettingsToUi();
            _settingsLoaded = true;
            ConfigureTrayMode();
            await InitializeAutoBackupAsync();
            ConfigureAutoBackupTimers();
        }
        catch (Exception exception)
        {
            ShowNotice(LocalizationService.Get("ReadFailed"), exception.Message, InfoBarSeverity.Error);
        }
    }

    private void RefreshEnvironmentSummary()
    {
        var environment = _layoutService.ReadEnvironment();
        HeroEnvironmentText.Text = $"{environment.VirtualWidth} × {environment.VirtualHeight}  ·  {LocalizationService.Format("MonitorCountFormat", environment.MonitorCount)}";
        HeroDpiText.Text = $"DPI {environment.Dpi}  ·  {environment.Dpi / 96d:P0}";
        MonitorCountText.Text = environment.MonitorCount.ToString();
        App.Log($"Display environment: {environment.VirtualWidth}x{environment.VirtualHeight}, DPI {environment.Dpi}, monitors {environment.MonitorCount}");
        foreach (var monitor in environment.Monitors)
        {
            App.Log($"Monitor: {monitor.Name}, device={monitor.DeviceName}, bounds={monitor.Left},{monitor.Top},{monitor.Width}x{monitor.Height}, primary={monitor.IsPrimary}");
        }
    }

    private void RefreshBackupItems()
    {
        BackupTimelineGroups.Clear();

        var backupsById = _backups.ToDictionary(item => item.Id);
        var childrenByParent = _backups
            .Where(item => item.RelatedBackupId is Guid parentId && backupsById.ContainsKey(parentId))
            .GroupBy(item => item.RelatedBackupId!.Value)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(item => item.CreatedAt).ToList());
        var childIds = childrenByParent.Values.SelectMany(items => items).Select(item => item.Id).ToHashSet();
        var rootBackups = _backups.Where(item => !childIds.Contains(item.Id));

        foreach (var dateGroup in rootBackups
                     .OrderByDescending(item => item.CreatedAt)
                     .GroupBy(item => item.CreatedAt.LocalDateTime.Date))
        {
            var group = new BackupTimelineGroup { Header = GetTimelineDateHeader(dateGroup.Key) };
            foreach (var backup in dateGroup)
            {
                AppendBackupTree(group, backup, 0, backupsById, childrenByParent, []);
            }

            BackupTimelineGroups.Add(group);
        }

        BackupCountText.Text = _backups.Count.ToString();
        EmptyBackupsText.Visibility = _backups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        BackupsList.Visibility = _backups.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        var latest = _backups.OrderByDescending(item => item.CreatedAt).FirstOrDefault();
        LastBackupText.Text = latest is null ? LocalizationService.Get("NoBackupsYet") : latest.CreatedAt.LocalDateTime.ToString("g");
        HeroIconCountText.Text = latest is null ? LocalizationService.Get("WaitingFirstBackup") : LocalizationService.Format("LatestIconCountFormat", latest.Icons.Count);
    }

    private static void AppendBackupTree(
        BackupTimelineGroup group,
        DesktopLayoutBackup backup,
        int depth,
        IReadOnlyDictionary<Guid, DesktopLayoutBackup> backupsById,
        IReadOnlyDictionary<Guid, List<DesktopLayoutBackup>> childrenByParent,
        HashSet<Guid> visited)
    {
        if (!visited.Add(backup.Id))
        {
            return;
        }

        group.Add(new BackupListItem
        {
            Backup = backup,
            HierarchyDepth = depth,
            RelatedBackupName = backup.RelatedBackupId is Guid relatedId && backupsById.TryGetValue(relatedId, out var relatedBackup)
                ? relatedBackup.Name
                : string.Empty
        });

        if (!childrenByParent.TryGetValue(backup.Id, out var children))
        {
            return;
        }

        foreach (var child in children)
        {
            AppendBackupTree(group, child, depth + 1, backupsById, childrenByParent, visited);
        }
    }

    private static string GetTimelineDateHeader(DateTime date)
    {
        var today = DateTime.Today;
        if (date == today) return LocalizationService.Format("TodayDateFormat", date.ToString("M"));
        if (date == today.AddDays(-1)) return LocalizationService.Format("YesterdayDateFormat", date.ToString("M"));
        return date.ToString("D");
    }

    private async void CreateBackup_Click(object sender, RoutedEventArgs e)
    {
        var defaultName = LocalizationService.Format("DesktopLayoutNameFormat", DateTime.Now.ToString("g"));
        var nameBox = new TextBox
        {
            Header = LocalizationService.Get("BackupNameHeader"),
            Text = defaultName,
            SelectionStart = 0,
            SelectionLength = defaultName.Length
        };
        var noteBox = new TextBox
        {
            Header = LocalizationService.Get("BackupNoteHeader"),
            PlaceholderText = LocalizationService.Get("BackupNotePlaceholder")
        };
        var panel = new StackPanel { Spacing = 14 };
        panel.Children.Add(nameBox);
        panel.Children.Add(noteBox);

        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = LocalizationService.Get("SaveLayoutTitle"),
            Content = panel,
            PrimaryButtonText = LocalizationService.Get("CreateBackup"),
            CloseButtonText = LocalizationService.Get("Cancel"),
            DefaultButton = ContentDialogButton.Primary
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var backupName = string.IsNullOrWhiteSpace(nameBox.Text)
            ? defaultName
            : nameBox.Text.Trim();
        var backupNote = noteBox.Text.Trim();

        SetBusy(true, LocalizationService.Get("ReadingIcons"));
        try
        {
            var backup = await Task.Run(() => _layoutService.Capture(backupName, backupNote));
            App.Log($"Create backup: captured {backup.Icons.Count} icons");
            _backups.Add(backup);
            await _store.SaveAsync(_backups);
            App.Log($"Create backup: saved {backup.Id} to {_store.FilePath}");
            RefreshBackupItems();
            ShowNotice(LocalizationService.Get("BackupCompleted"), LocalizationService.Format("BackupCompletedMessage", backup.Icons.Count), InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            App.Log($"Create backup failed: {exception}");
            ShowNotice(LocalizationService.Get("BackupFailed"), exception.Message, InfoBarSeverity.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void RestoreBackup_Click(object sender, RoutedEventArgs e)
    {
        if (GetSingleSelectedBackup() is not BackupListItem selected)
        {
            return;
        }

        await RestoreBackupAsync(selected.Backup, RootGrid.XamlRoot);
    }

    internal Task<bool> RestoreBackupFromPreviewAsync(DesktopLayoutBackup backup, XamlRoot xamlRoot) =>
        RestoreBackupAsync(backup, xamlRoot);

    private async Task<bool> RestoreBackupAsync(DesktopLayoutBackup backup, XamlRoot xamlRoot)
    {

        var restoreContent = new StackPanel { Spacing = 12 };
        restoreContent.Children.Add(new TextBlock
        {
            Text = LocalizationService.Format("RestoreConfirm", backup.Name),
            TextWrapping = TextWrapping.Wrap
        });

        CheckBox? followMonitorsCheckBox = null;
        var supportsMonitorMapping = backup.Environment.Monitors.Count > 0 &&
                                     backup.Icons.Any(icon => !string.IsNullOrWhiteSpace(icon.MonitorId));
        if (supportsMonitorMapping)
        {
            followMonitorsCheckBox = new CheckBox
            {
                Content = LocalizationService.Get("RestoreFollowMonitors"),
                IsChecked = true
            };
            restoreContent.Children.Add(followMonitorsCheckBox);
            restoreContent.Children.Add(new TextBlock
            {
                Text = LocalizationService.Get("RestoreFollowMonitorsDescription"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(28, -8, 0, 0),
                FontSize = 12,
                Opacity = 0.7
            });
        }

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = LocalizationService.Get("RestoreTitle"),
            Content = restoreContent,
            PrimaryButtonText = LocalizationService.Get("Restore"),
            CloseButtonText = LocalizationService.Get("Cancel"),
            DefaultButton = ContentDialogButton.Close
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return false;
        }

        _automaticBackupsSuspended = true;
        SetBusy(true, LocalizationService.Get("CreatingSafetyBackup"));
        var restoredSuccessfully = false;
        try
        {
            var safetyName = $"{LocalizationService.Get("SafetyBackup")} {DateTime.Now:g}";
            var safetyBackup = await Task.Run(() => _layoutService.Capture(
                safetyName,
                LocalizationService.Format("SafetySummaryNamed", backup.Name),
                true));
            safetyBackup.RelatedBackupId = backup.Id;
            safetyBackup.TriggerReason = "pre-restore";
            _backups.Add(safetyBackup);
            await _store.SaveAsync(_backups);

            BusyText.Text = LocalizationService.Get("RestoringIcons");
            var followMonitorPositions = followMonitorsCheckBox?.IsChecked == true;
            var result = await Task.Run(() => _layoutService.Restore(backup, followMonitorPositions));
            RefreshBackupItems();

            var severity = result.Failed == 0 ? InfoBarSeverity.Success : InfoBarSeverity.Warning;
            var message = followMonitorPositions
                ? LocalizationService.Format("RestoreResultMapped", result.Restored, result.Missing, result.Failed, result.Remapped)
                : LocalizationService.Format("RestoreResult", result.Restored, result.Missing, result.Failed);
            ShowNotice(LocalizationService.Get("RestoreCompleted"), message, severity);
            restoredSuccessfully = true;
        }
        catch (Exception exception)
        {
            ShowNotice(LocalizationService.Get("RestoreFailed"), exception.Message, InfoBarSeverity.Error);
        }
        finally
        {
            if (_settings.AutoBackupEnabled)
            {
                // Explorer applies icon positions asynchronously. Wait briefly,
                // then treat the restored desktop as the new event baseline.
                await Task.Delay(750);
                await EstablishAutoBackupBaselineAsync();
            }

            _automaticBackupsSuspended = false;
            SetBusy(false);
        }

        return restoredSuccessfully;
    }

    private async void DeleteBackup_Click(object sender, RoutedEventArgs e)
    {
        var selectedItems = BackupsList.SelectedItems.OfType<BackupListItem>().ToList();
        if (selectedItems.Count == 0)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = selectedItems.Count == 1
                ? LocalizationService.Get("DeleteTitle")
                : LocalizationService.Get("DeleteMultipleTitle"),
            Content = selectedItems.Count == 1
                ? LocalizationService.Format("DeleteConfirm", selectedItems[0].Name)
                : LocalizationService.Format("DeleteMultipleConfirm", selectedItems.Count),
            PrimaryButtonText = LocalizationService.Get("Delete"),
            CloseButtonText = LocalizationService.Get("Cancel"),
            DefaultButton = ContentDialogButton.Close
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var selectedIds = selectedItems.Select(item => item.Backup.Id).ToHashSet();
        _backups.RemoveAll(item => selectedIds.Contains(item.Id));
        await _store.SaveAsync(_backups);
        RefreshBackupItems();
        ShowNotice(
            LocalizationService.Get("BackupDeleted"),
            selectedItems.Count == 1
                ? LocalizationService.Get("BackupDeletedMessage")
                : LocalizationService.Format("BackupsDeletedMessage", selectedItems.Count),
            InfoBarSeverity.Success);
    }

    private void BackupsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncingBackupSelection)
        {
            _syncingBackupSelection = true;
            try
            {
                foreach (var addedParent in e.AddedItems.OfType<BackupListItem>().ToList())
                {
                    foreach (var descendant in GetBackupDescendants(addedParent))
                    {
                        if (!BackupsList.SelectedItems.Contains(descendant))
                        {
                            BackupsList.SelectedItems.Add(descendant);
                        }
                    }
                }

                foreach (var removedParent in e.RemovedItems.OfType<BackupListItem>().ToList())
                {
                    foreach (var descendant in GetBackupDescendants(removedParent))
                    {
                        BackupsList.SelectedItems.Remove(descendant);
                    }
                }
            }
            finally
            {
                _syncingBackupSelection = false;
            }
        }

        var selectedItems = BackupsList.SelectedItems.OfType<BackupListItem>().ToList();
        var selectedSet = selectedItems.ToHashSet();
        foreach (var item in BackupTimelineGroups.SelectMany(group => group))
        {
            item.IsSelected = selectedSet.Contains(item);
        }

        var singleSelection = selectedItems.Count == 1 ? selectedItems[0] : null;
        RestoreBackupButton.IsEnabled = singleSelection is not null;
        DeleteBackupButton.IsEnabled = selectedItems.Count > 0;
        ViewBackupButton.IsEnabled = singleSelection is not null;
        SelectionHintText.Text = selectedItems.Count switch
        {
            0 => LocalizationService.Get("SelectionHintDynamic"),
            1 => LocalizationService.Format("SelectedFormat", singleSelection!.Name),
            _ => LocalizationService.Format("SelectedCountFormat", selectedItems.Count)
        };
    }

    private void ViewBackup_Click(object sender, RoutedEventArgs e) => OpenSelectedBackupPreview();

    private void BackupContent_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        // The content side is for reading and previewing. Selection is changed
        // only through the dedicated area on the left.
        e.Handled = true;
    }

    private void BackupContent_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: BackupListItem item })
        {
            OpenBackupPreview(item);
        }

        e.Handled = true;
    }

    private void OpenSelectedBackupPreview()
    {
        if (GetSingleSelectedBackup() is not BackupListItem selected)
        {
            return;
        }

        OpenBackupPreview(selected);
    }

    private void OpenBackupPreview(BackupListItem item)
    {
        var previewWindow = new BackupPreviewWindow(item.Backup, this);
        _previewWindows.Add(previewWindow);
        previewWindow.Closed += (_, _) => _previewWindows.Remove(previewWindow);
        previewWindow.Activate();
    }

    private BackupListItem? GetSingleSelectedBackup()
    {
        var selectedItems = BackupsList.SelectedItems.OfType<BackupListItem>().Take(2).ToList();
        return selectedItems.Count == 1 ? selectedItems[0] : null;
    }

    private IReadOnlyList<BackupListItem> GetBackupDescendants(BackupListItem parent)
    {
        var displayedItems = BackupTimelineGroups.SelectMany(group => group).ToList();
        var itemsById = displayedItems.ToDictionary(item => item.Backup.Id);
        return BackupRelationshipService.GetDescendantIds(_backups, parent.Backup.Id)
            .Where(itemsById.ContainsKey)
            .Select(id => itemsById[id])
            .ToList();
    }

    private void AppNavigationView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var tag = args.SelectedItemContainer?.Tag?.ToString();
        ShowSection(tag switch
        {
            "backups" => BackupsSection,
            "settings" => SettingsSection,
            "about" => AboutSection,
            _ => DashboardSection
        });
    }

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
        AboutSection.Visibility = section == AboutSection ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplySettingsToUi()
    {
        AutoBackupToggle.IsOn = _settings.AutoBackupEnabled;
        ScheduledBackupToggle.IsOn = _settings.ScheduledBackupEnabled;
        StartupTriggerCheckBox.IsChecked = _settings.BackupOnStartup;
        DisplayTriggerCheckBox.IsChecked = _settings.BackupOnDisplayChange;
        DesktopTriggerCheckBox.IsChecked = _settings.BackupOnDesktopChange;
        _settings.AutomaticBackupRetention = Math.Clamp(_settings.AutomaticBackupRetention, 1, 200);
        AutomaticBackupRetentionNumberBox.Value = _settings.AutomaticBackupRetention;
        RunAtStartupToggle.IsOn = _settings.RunAtStartup;
        MinimizeToTrayToggle.IsOn = _settings.MinimizeToTray;

        var language = LocalizationService.NormalizeLanguage(_settings.UiLanguage);
        LanguageComboBox.SelectedItem = LanguageComboBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => item.Tag?.ToString() == language)
            ?? LanguageComboBox.Items.OfType<ComboBoxItem>().First();

        foreach (var item in BackupIntervalComboBox.Items.OfType<ComboBoxItem>())
        {
            if (int.TryParse(item.Tag?.ToString(), out var minutes) && minutes == _settings.BackupIntervalMinutes)
            {
                BackupIntervalComboBox.SelectedItem = item;
                break;
            }
        }

        UpdateAutoBackupControls();
        UpdateBackgroundModeStatus();
    }

    private async void BackgroundSettings_Changed(object sender, RoutedEventArgs e)
    {
        if (!_settingsLoaded)
        {
            return;
        }

        var previousStartup = _settings.RunAtStartup;
        var previousTrayMode = _settings.MinimizeToTray;
        var requestedStartup = RunAtStartupToggle.IsOn;
        var requestedTrayMode = MinimizeToTrayToggle.IsOn;
        try
        {
            if (requestedStartup != previousStartup)
            {
                await _startupService.SetEnabledAsync(requestedStartup);
            }
            _settings.RunAtStartup = requestedStartup;
            _settings.MinimizeToTray = requestedTrayMode;
            ConfigureTrayMode();
            await _settingsStore.SaveAsync(_settings);
            UpdateBackgroundModeStatus();
        }
        catch (Exception exception)
        {
            try
            {
                await _startupService.SetEnabledAsync(previousStartup);
            }
            catch (Exception rollbackException)
            {
                App.Log($"Unable to roll back startup setting: {rollbackException}");
            }
            _settings.RunAtStartup = previousStartup;
            _settings.MinimizeToTray = previousTrayMode;
            RunAtStartupToggle.IsOn = previousStartup;
            MinimizeToTrayToggle.IsOn = previousTrayMode;
            ConfigureTrayMode();
            ShowNotice(LocalizationService.Get("BackgroundSettingsFailed"), exception.Message, InfoBarSeverity.Error);
        }
    }

    private async void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_settingsLoaded || LanguageComboBox.SelectedItem is not ComboBoxItem selected)
        {
            return;
        }

        var language = LocalizationService.NormalizeLanguage(selected.Tag?.ToString());
        if (language == LocalizationService.NormalizeLanguage(_settings.UiLanguage))
        {
            return;
        }

        _settings.UiLanguage = language;
        await _settingsStore.SaveAsync(_settings);
        LocalizationService.ApplyLanguage(language);
        Microsoft.Windows.AppLifecycle.AppInstance.Restart(string.Empty);
    }

    private void ConfigureTrayMode()
    {
        if (_settings.MinimizeToTray && _trayIconService is null)
        {
            throw new InvalidOperationException(LocalizationService.Get("TrayUnavailable"));
        }

        _trayIconService?.SetVisible(_settings.MinimizeToTray);
    }

    private void UpdateBackgroundModeStatus()
    {
        var modes = new List<string>();
        if (_settings.RunAtStartup) modes.Add(LocalizationService.Get("RunAtStartupEnabled"));
        if (_settings.MinimizeToTray) modes.Add(LocalizationService.Get("TrayModeEnabled"));
        BackgroundModeStatusText.Text = modes.Count == 0
            ? LocalizationService.Get("BackgroundModeOff")
            : string.Join(" · ", modes);
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

    private async void AutomaticBackupRetentionNumberBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_settingsLoaded || double.IsNaN(sender.Value))
        {
            return;
        }

        var retention = Math.Clamp((int)Math.Round(sender.Value), 1, 200);
        sender.Value = retention;
        if (_settings.AutomaticBackupRetention == retention)
        {
            return;
        }

        _settings.AutomaticBackupRetention = retention;
        await _settingsStore.SaveAsync(_settings);
        if (TrimAutomaticBackupsToRetention())
        {
            await _store.SaveAsync(_backups);
            RefreshBackupItems();
        }
    }

    private void UpdateSettingsFromUi()
    {
        _settings.AutoBackupEnabled = AutoBackupToggle.IsOn;
        _settings.ScheduledBackupEnabled = ScheduledBackupToggle.IsOn;
        _settings.BackupOnStartup = StartupTriggerCheckBox.IsChecked == true;
        _settings.BackupOnDisplayChange = DisplayTriggerCheckBox.IsChecked == true;
        _settings.BackupOnDesktopChange = DesktopTriggerCheckBox.IsChecked == true;
        if (!double.IsNaN(AutomaticBackupRetentionNumberBox.Value))
        {
            _settings.AutomaticBackupRetention = Math.Clamp((int)Math.Round(AutomaticBackupRetentionNumberBox.Value), 1, 200);
        }

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
            AutoBackupStatusText.Text = LocalizationService.Get("AutoBackupOff");
            return;
        }

        var modes = new List<string>();
        if (_settings.ScheduledBackupEnabled)
        {
            modes.Add(LocalizationService.Format("EveryMinutesFormat", _settings.BackupIntervalMinutes));
        }
        if (_settings.BackupOnStartup) modes.Add(LocalizationService.Get("ReasonStartup"));
        if (_settings.BackupOnDisplayChange) modes.Add(LocalizationService.Get("ReasonDisplayChange"));
        if (_settings.BackupOnDesktopChange) modes.Add(LocalizationService.Get("ReasonDesktopChange"));
        AutoBackupStatusText.Text = modes.Count == 0
            ? LocalizationService.Get("AutoNoTrigger")
            : LocalizationService.Format("EnabledModesFormat", string.Join(" · ", modes));
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
            await CreateAutomaticBackupAsync("startup", snapshot);
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
        if (_automaticBackupsSuspended)
        {
            return;
        }

        await CreateAutomaticBackupAsync("schedule");
    }

    private async void EventWatchTimer_Tick(object? sender, object e)
    {
        if (_automaticBackupsSuspended || _autoBackupBusy || !_settings.AutoBackupEnabled)
        {
            return;
        }

        _autoBackupBusy = true;
        try
        {
            var snapshot = await CaptureAutomaticSnapshotAsync();
            if (snapshot is null || _automaticBackupsSuspended)
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
                await SaveAutomaticBackupCoreAsync("display-change", snapshot);
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
                    await SaveAutomaticBackupCoreAsync("desktop-change", snapshot);
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
        if (_automaticBackupsSuspended || _autoBackupBusy || !_settings.AutoBackupEnabled)
        {
            return;
        }

        _autoBackupBusy = true;
        try
        {
            snapshot ??= await CaptureAutomaticSnapshotAsync();
            if (snapshot is not null && !_automaticBackupsSuspended)
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
            return await Task.Run(() => _layoutService.Capture(LocalizationService.Get("AutomaticBackup")));
        }
        catch (Exception exception)
        {
            App.Log($"Automatic backup capture failed: {exception}");
            AutoBackupStatusText.Text = LocalizationService.Format("AutoBackupFailedFormat", exception.Message);
            return null;
        }
    }

    private async Task SaveAutomaticBackupCoreAsync(string reason, DesktopLayoutBackup snapshot)
    {
        var reasonText = GetReasonText(reason);
        snapshot.Name = $"{LocalizationService.Get("AutomaticBackup")} · {reasonText} {DateTime.Now:g}";
        snapshot.Note = LocalizationService.Format("AutomaticReasonFormat", reasonText);
        snapshot.CreatedAt = DateTimeOffset.Now;
        snapshot.IsAutomaticBackup = true;
        snapshot.IsSafetyBackup = false;
        snapshot.TriggerReason = reason;
        _backups.Add(snapshot);

        TrimAutomaticBackupsToRetention();

        await _store.SaveAsync(_backups);
        SetAutoBackupBaseline(snapshot);
        RefreshBackupItems();
        AutoBackupStatusText.Text = LocalizationService.Format("LastAutoBackupFormat", DateTime.Now.ToString("T"), reasonText);
        App.Log($"Automatic backup saved: {snapshot.Id}, reason={reason}, icons={snapshot.Icons.Count}");
    }

    private bool TrimAutomaticBackupsToRetention()
    {
        return BackupRetentionService.TrimAutomaticBackups(
            _backups,
            _settings.AutomaticBackupRetention).Count > 0;
    }

    private static string GetReasonText(string reason) => reason switch
    {
        "startup" => LocalizationService.Get("ReasonStartup"),
        "schedule" => LocalizationService.Get("ReasonSchedule"),
        "display-change" => LocalizationService.Get("ReasonDisplayChange"),
        "desktop-change" => LocalizationService.Get("ReasonDesktopChange"),
        _ => reason
    };

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

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (!_isExiting && _settingsLoaded && _settings.MinimizeToTray)
        {
            args.Cancel = true;
            HideToTray();
        }
    }

    private void HideToTray()
    {
        ClosePreviewWindows();
        ShowWindow(_windowHandle, 0);
        _trayIconService?.ShowNotification(LocalizationService.Get("TrayRunningTitle"), LocalizationService.Get("TrayRunningMessage"));
    }

    private void ShowFromTray()
    {
        ShowWindow(_windowHandle, 5);
        Activate();
        SetForegroundWindow(_windowHandle);
    }

    private async void TrayIcon_BackupRequested()
    {
        if (_trayBackupBusy || _autoBackupBusy)
        {
            _trayIconService?.ShowNotification("DeskSnapshot", LocalizationService.Get("TrayBusy"));
            return;
        }

        _trayBackupBusy = true;
        try
        {
            var now = DateTime.Now;
            var backup = await Task.Run(() => _layoutService.Capture(
                LocalizationService.Format("TrayBackupNameFormat", now.ToString("g")),
                LocalizationService.Get("TrayBackupNote")));
            backup.CreatedAt = DateTimeOffset.Now;
            backup.TriggerReason = "tray-manual";
            _backups.Add(backup);
            await _store.SaveAsync(_backups);
            if (_settings.AutoBackupEnabled)
            {
                SetAutoBackupBaseline(backup);
            }
            RefreshBackupItems();
            _trayIconService?.ShowNotification(LocalizationService.Get("BackupCompleted"), LocalizationService.Format("BackupCompletedMessage", backup.Icons.Count));
        }
        catch (Exception exception)
        {
            App.Log($"Tray backup failed: {exception}");
            _trayIconService?.ShowNotification(LocalizationService.Get("BackupFailed"), exception.Message);
        }
        finally
        {
            _trayBackupBusy = false;
        }
    }

    private void ExitFromTray()
    {
        _isExiting = true;
        ClosePreviewWindows();
        _trayIconService?.SetVisible(false);
        Close();
    }

    private void ClosePreviewWindows()
    {
        foreach (var previewWindow in _previewWindows.ToArray())
        {
            previewWindow.Close();
        }
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        _scheduledBackupTimer.Stop();
        _eventWatchTimer.Stop();
        ClosePreviewWindows();
        _trayIconService?.Dispose();
        _trayIconService = null;
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

    private void SetBusy(bool isBusy, string? message = null)
    {
        BusyText.Text = message ?? LocalizationService.Get("BusyText");
        BusyOverlay.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowNotice(string title, string message, InfoBarSeverity severity)
    {
        ActionInfoBar.Title = title;
        ActionInfoBar.Message = message;
        ActionInfoBar.Severity = severity;
        ActionInfoBar.IsOpen = true;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);
}
