using DeskSnapshot.Models;
using DeskSnapshot.Services;
using System.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System.Runtime.InteropServices;
using Windows.Graphics;

namespace DeskSnapshot;

public sealed partial class BackupPreviewWindow : Window
{
    private const double IconSize = 20;
    private const double Inset = 14;
    private const double FramePadding = 36;
    private readonly DesktopLayoutBackup _backup;
    private readonly MainWindow _owner;
    private readonly DesktopIconLayoutService _layoutService = new();
    private DesktopLayoutBackup? _currentDesktop;
    private DesktopLayoutComparisonResult? _comparison;
    private bool _comparisonStarted;

    public BackupPreviewWindow(DesktopLayoutBackup backup, MainWindow owner)
    {
        _backup = backup;
        _owner = owner;
        InitializeComponent();
        LocalizationBindings.Apply(RootGrid);
        RootGrid.Loaded += RootGrid_Loaded;
        Title = LocalizationService.Format("PreviewTitleFormat", backup.Name);

        try
        {
            SystemBackdrop = new MicaBackdrop();
        }
        catch
        {
            // Mica 不可用时保留默认背景。
        }

        ConfigureWindow(owner, backup);
        PopulateHeader(backup);
    }

    private void ConfigureWindow(Window owner, DesktopLayoutBackup backup)
    {
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(handle);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        App.ApplyWindowIcon(appWindow);
        var ownerHandle = WinRT.Interop.WindowNative.GetWindowHandle(owner);
        var ownerWindowId = Win32Interop.GetWindowIdFromWindow(ownerHandle);
        var workArea = DisplayArea.GetFromWindowId(ownerWindowId, DisplayAreaFallback.Nearest).WorkArea;

        // Make this an owned window so it stays with the main window on the same
        // display and cannot be placed behind it or on another desktop area.
        SetWindowLongPtr(handle, GwlpHwndParent, ownerHandle);

        var desktopAspect = Math.Max(0.5, backup.Environment.VirtualWidth / (double)Math.Max(1, backup.Environment.VirtualHeight));
        var dpiScale = Math.Max(1d, GetDpiForWindow(ownerHandle) / 96d);
        var horizontalChrome = 96 * dpiScale;
        var monitorDetailsHeight = backup.Environment.Monitors.Count > 0 ? 24 : 0;
        var verticalChrome = ((string.IsNullOrWhiteSpace(backup.Note) ? 280 : 305) + monitorDetailsHeight) * dpiScale;
        var maxWindowWidth = workArea.Width * 0.90;
        var maxWindowHeight = workArea.Height * 0.90;
        var maxPreviewWidth = Math.Max(400, maxWindowWidth - horizontalChrome);
        var maxPreviewHeight = Math.Max(300, maxWindowHeight - verticalChrome);

        var previewWidth = maxPreviewWidth;
        var previewHeight = previewWidth / desktopAspect;
        if (previewHeight > maxPreviewHeight)
        {
            previewHeight = maxPreviewHeight;
            previewWidth = previewHeight * desktopAspect;
        }

        var width = (int)Math.Ceiling(previewWidth + horizontalChrome);
        var height = (int)Math.Ceiling(previewHeight + verticalChrome);
        var x = workArea.X + (workArea.Width - width) / 2;
        var y = workArea.Y + (workArea.Height - height) / 2;
        appWindow.MoveAndResize(new RectInt32(x, y, width, height));
    }

    private void PopulateHeader(DesktopLayoutBackup backup)
    {
        var environment = backup.Environment;
        BackupNameText.Text = backup.Name;
        BackupSummaryText.Text = LocalizationService.Format(
            "PreviewSummaryFormat",
            backup.CreatedAt.LocalDateTime.ToString("g"),
            backup.Icons.Count,
            environment.VirtualWidth,
            environment.VirtualHeight,
            environment.Dpi,
            environment.MonitorCount);
        PreviewHintText.Text = LocalizationService.Get("PreviewHint");

        if (environment.Monitors.Count > 0)
        {
            var monitorNames = string.Join("  ·  ", environment.Monitors.Select(monitor =>
                $"{monitor.Name} ({monitor.Width} × {monitor.Height})"));
            BackupMonitorNamesText.Text = LocalizationService.Format("PreviewMonitorsFormat", monitorNames);
            BackupMonitorNamesText.Visibility = Visibility.Visible;
        }

        if (!string.IsNullOrWhiteSpace(backup.Note))
        {
            BackupNoteText.Text = LocalizationService.Format("PreviewNoteFormat", backup.Note);
            BackupNoteText.Visibility = Visibility.Visible;
        }
    }

    private async void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        LocalizationBindings.Apply(RootGrid);
        if (_comparisonStarted)
        {
            return;
        }

        _comparisonStarted = true;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            _currentDesktop = await Task.Run(() => _layoutService.Capture(LocalizationService.Get("ComparisonCurrentLayout")));
            _comparison = DesktopLayoutComparisonService.Compare(_backup, _currentDesktop);
            UpdateComparisonSummary(_comparison);
            DifferenceLegend.Visibility = Visibility.Visible;
            ShowChangesOnlyCheckBox.IsEnabled = true;
            DrawLayout(PreviewSurface.ActualWidth, PreviewSurface.ActualHeight);
            App.Log($"Layout comparison completed for {_backup.Id}: {_comparison.Differences.Count} icons in {stopwatch.ElapsedMilliseconds}ms");
        }
        catch (Exception exception)
        {
            ComparisonSummaryText.Text = LocalizationService.Format("ComparisonFailedFormat", exception.Message);
            EnvironmentDifferenceText.Visibility = Visibility.Collapsed;
            AmbiguousDifferenceText.Visibility = Visibility.Collapsed;
            App.Log($"Layout comparison failed for {_backup.Id}: {exception}");
        }
        finally
        {
            ComparisonProgress.IsActive = false;
            ComparisonProgress.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateComparisonSummary(DesktopLayoutComparisonResult comparison)
    {
        ComparisonSummaryText.Text = LocalizationService.Format(
            "ComparisonSummaryFormat",
            comparison.MovedCount,
            comparison.AddedCount,
            comparison.MissingCount,
            comparison.UnchangedCount);

        var environmentChanges = new List<string>();
        if (comparison.VirtualSizeChanged) environmentChanges.Add(LocalizationService.Get("DifferenceVirtualSize"));
        if (comparison.DpiChanged) environmentChanges.Add(LocalizationService.Get("DifferenceDpi"));
        if (comparison.MonitorCountChanged) environmentChanges.Add(LocalizationService.Get("DifferenceMonitorCount"));
        if (comparison.MonitorLayoutChanged) environmentChanges.Add(LocalizationService.Get("DifferenceMonitorLayout"));
        EnvironmentDifferenceText.Text = environmentChanges.Count == 0
            ? LocalizationService.Get("EnvironmentMatches")
            : LocalizationService.Format("EnvironmentDifferencesFormat", string.Join(" · ", environmentChanges));
        EnvironmentDifferenceText.Visibility = Visibility.Visible;

        if (comparison.AmbiguousCount > 0)
        {
            AmbiguousDifferenceText.Text = LocalizationService.Format("AmbiguousMatchesFormat", comparison.AmbiguousCount);
            AmbiguousDifferenceText.Visibility = Visibility.Visible;
        }
        else
        {
            AmbiguousDifferenceText.Visibility = Visibility.Collapsed;
        }
    }

    private void PreviewHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var maxContentWidth = Math.Max(1, e.NewSize.Width - FramePadding);
        var maxContentHeight = Math.Max(1, e.NewSize.Height - FramePadding);
        var desktopAspect = Math.Max(0.5, _backup.Environment.VirtualWidth / (double)Math.Max(1, _backup.Environment.VirtualHeight));

        var contentWidth = maxContentWidth;
        var contentHeight = contentWidth / desktopAspect;
        if (contentHeight > maxContentHeight)
        {
            contentHeight = maxContentHeight;
            contentWidth = contentHeight * desktopAspect;
        }

        PreviewFrame.Width = contentWidth + FramePadding;
        PreviewFrame.Height = contentHeight + FramePadding;
    }

    private void PreviewSurface_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        DrawLayout(e.NewSize.Width, e.NewSize.Height);
    }

    private void DrawLayout(double canvasWidth, double canvasHeight)
    {
        if (canvasWidth <= Inset * 2 || canvasHeight <= Inset * 2)
        {
            return;
        }

        LayoutCanvas.Children.Clear();
        var usableWidth = canvasWidth - Inset * 2;
        var usableHeight = canvasHeight - Inset * 2;

        if (_comparison is null || _currentDesktop is null)
        {
            if (_backup.Icons.Count == 0)
            {
                ShowEmptyMessage(LocalizationService.Get("PreviewEmptyText"));
                return;
            }

            EmptyPreviewText.Visibility = Visibility.Collapsed;
            foreach (var icon in _backup.Icons.OrderBy(item => item.CaptureOrder))
            {
                var tile = CreateDifferenceTile(DesktopIconDifferenceKind.Unchanged, false);
                ToolTipService.SetToolTip(tile, $"{icon.Name}\nX {icon.X}  ·  Y {icon.Y}");
                PositionElement(tile, icon, _backup.Environment, usableWidth, usableHeight);
                LayoutCanvas.Children.Add(tile);
            }
            return;
        }

        var showChangesOnly = ShowChangesOnlyCheckBox.IsChecked == true;
        var differences = _comparison.Differences
            .Where(item => !showChangesOnly || item.Kind != DesktopIconDifferenceKind.Unchanged)
            .OrderBy(item => item.Kind == DesktopIconDifferenceKind.Unchanged ? 0 : 1)
            .ThenBy(item => item.BackupIcon?.CaptureOrder ?? item.CurrentIcon?.CaptureOrder ?? int.MaxValue)
            .ToList();
        if (differences.Count == 0)
        {
            ShowEmptyMessage(LocalizationService.Get(showChangesOnly ? "NoDifferences" : "PreviewEmptyText"));
            return;
        }

        EmptyPreviewText.Visibility = Visibility.Collapsed;
        foreach (var difference in differences.Where(item => item.Kind == DesktopIconDifferenceKind.Moved))
        {
            DrawMovement(difference, usableWidth, usableHeight);
        }

        foreach (var difference in differences)
        {
            var icon = difference.Kind == DesktopIconDifferenceKind.Added
                ? difference.CurrentIcon
                : difference.BackupIcon;
            var environment = difference.Kind == DesktopIconDifferenceKind.Added
                ? _currentDesktop.Environment
                : _backup.Environment;
            if (icon is null)
            {
                continue;
            }

            var tile = CreateDifferenceTile(difference.Kind, difference.IsAmbiguous);
            ToolTipService.SetToolTip(tile, BuildDifferenceTooltip(difference));
            PositionElement(tile, icon, environment, usableWidth, usableHeight);
            LayoutCanvas.Children.Add(tile);
        }

        App.Log($"Backup preview rendered {differences.Count} comparison icons for {_backup.Id} at {canvasWidth:F0}x{canvasHeight:F0}");
    }

    private static Border CreateDifferenceTile(DesktopIconDifferenceKind kind, bool isAmbiguous)
    {
        var (background, border, foreground, glyph) = kind switch
        {
            DesktopIconDifferenceKind.Moved =>
                (ColorHelper.FromArgb(245, 255, 238, 204), ColorHelper.FromArgb(255, 255, 176, 32), ColorHelper.FromArgb(255, 126, 76, 0), "\uE72A"),
            DesktopIconDifferenceKind.Added =>
                (ColorHelper.FromArgb(245, 218, 248, 237), ColorHelper.FromArgb(255, 46, 189, 133), ColorHelper.FromArgb(255, 5, 100, 67), "\uE710"),
            DesktopIconDifferenceKind.Missing =>
                (ColorHelper.FromArgb(245, 255, 224, 221), ColorHelper.FromArgb(255, 240, 68, 56), ColorHelper.FromArgb(255, 145, 25, 18), "\uE711"),
            _ =>
                (ColorHelper.FromArgb(230, 245, 249, 255), ColorHelper.FromArgb(190, 100, 165, 255), ColorHelper.FromArgb(255, 20, 70, 130), "\uE8B7")
        };

        var content = new Grid();
        content.Children.Add(new FontIcon
        {
            Glyph = glyph,
            FontSize = 10,
            Foreground = new SolidColorBrush(foreground)
        });
        if (isAmbiguous)
        {
            content.Children.Add(new Border
            {
                Width = 9,
                Height = 9,
                CornerRadius = new CornerRadius(5),
                Background = new SolidColorBrush(ColorHelper.FromArgb(255, 130, 82, 223)),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Child = new TextBlock
                {
                    Text = "?",
                    FontSize = 7,
                    Foreground = new SolidColorBrush(Colors.White),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            });
        }

        return new Border
        {
            Width = IconSize,
            Height = IconSize,
            CornerRadius = new CornerRadius(5),
            Background = new SolidColorBrush(background),
            BorderBrush = new SolidColorBrush(border),
            BorderThickness = new Thickness(isAmbiguous ? 2 : 1),
            Child = content
        };
    }

    private void DrawMovement(DesktopIconDifference difference, double usableWidth, double usableHeight)
    {
        if (difference.BackupIcon is null || difference.CurrentIcon is null || _currentDesktop is null)
        {
            return;
        }

        var backupPoint = GetCanvasPoint(difference.BackupIcon, _backup.Environment, usableWidth, usableHeight);
        var currentPoint = GetCanvasPoint(difference.CurrentIcon, _currentDesktop.Environment, usableWidth, usableHeight);
        LayoutCanvas.Children.Add(new Line
        {
            X1 = currentPoint.X + IconSize / 2,
            Y1 = currentPoint.Y + IconSize / 2,
            X2 = backupPoint.X + IconSize / 2,
            Y2 = backupPoint.Y + IconSize / 2,
            Stroke = new SolidColorBrush(ColorHelper.FromArgb(190, 255, 176, 32)),
            StrokeThickness = 1.5
        });

        var currentMarker = new Ellipse
        {
            Width = 8,
            Height = 8,
            Fill = new SolidColorBrush(ColorHelper.FromArgb(80, 255, 176, 32)),
            Stroke = new SolidColorBrush(ColorHelper.FromArgb(230, 255, 176, 32)),
            StrokeThickness = 1.5
        };
        Canvas.SetLeft(currentMarker, currentPoint.X + (IconSize - 8) / 2);
        Canvas.SetTop(currentMarker, currentPoint.Y + (IconSize - 8) / 2);
        LayoutCanvas.Children.Add(currentMarker);
    }

    private string BuildDifferenceTooltip(DesktopIconDifference difference)
    {
        var text = difference.Kind switch
        {
            DesktopIconDifferenceKind.Moved when difference.BackupIcon is not null && difference.CurrentIcon is not null =>
                LocalizationService.Format(
                    "DifferenceMovedTooltipFormat",
                    difference.Name,
                    difference.CurrentIcon.X,
                    difference.CurrentIcon.Y,
                    difference.BackupIcon.X,
                    difference.BackupIcon.Y),
            DesktopIconDifferenceKind.Added when difference.CurrentIcon is not null =>
                LocalizationService.Format("DifferenceAddedTooltipFormat", difference.Name, difference.CurrentIcon.X, difference.CurrentIcon.Y),
            DesktopIconDifferenceKind.Missing when difference.BackupIcon is not null =>
                LocalizationService.Format("DifferenceMissingTooltipFormat", difference.Name, difference.BackupIcon.X, difference.BackupIcon.Y),
            _ when difference.BackupIcon is not null =>
                LocalizationService.Format("DifferenceUnchangedTooltipFormat", difference.Name, difference.BackupIcon.X, difference.BackupIcon.Y),
            _ => difference.Name
        };

        return difference.IsAmbiguous
            ? $"{text}\n{LocalizationService.Get("AmbiguousTooltip")}"
            : text;
    }

    private static void PositionElement(
        FrameworkElement element,
        DesktopIconPosition icon,
        DesktopEnvironment environment,
        double usableWidth,
        double usableHeight)
    {
        var point = GetCanvasPoint(icon, environment, usableWidth, usableHeight);
        Canvas.SetLeft(element, point.X);
        Canvas.SetTop(element, point.Y);
    }

    private static (double X, double Y) GetCanvasPoint(
        DesktopIconPosition icon,
        DesktopEnvironment environment,
        double usableWidth,
        double usableHeight)
    {
        var coordinateWidth = Math.Max(1d, environment.VirtualWidth);
        var coordinateHeight = Math.Max(1d, environment.VirtualHeight);
        return (
            Inset + Math.Clamp(icon.X / coordinateWidth * usableWidth, 0, Math.Max(0, usableWidth - IconSize)),
            Inset + Math.Clamp(icon.Y / coordinateHeight * usableHeight, 0, Math.Max(0, usableHeight - IconSize)));
    }

    private void ShowEmptyMessage(string message)
    {
        EmptyPreviewText.Text = message;
        EmptyPreviewText.Visibility = Visibility.Visible;
    }

    private void ShowChangesOnly_Click(object sender, RoutedEventArgs e)
    {
        DrawLayout(PreviewSurface.ActualWidth, PreviewSurface.ActualHeight);
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        PreviewRestoreButton.IsEnabled = false;
        PreviewRestoreIcon.Visibility = Visibility.Collapsed;
        PreviewRestoreProgress.Visibility = Visibility.Visible;
        PreviewRestoreProgress.IsActive = true;
        try
        {
            if (await _owner.RestoreBackupFromPreviewAsync(_backup, RootGrid.XamlRoot))
            {
                Close();
            }
        }
        finally
        {
            PreviewRestoreProgress.IsActive = false;
            PreviewRestoreProgress.Visibility = Visibility.Collapsed;
            PreviewRestoreIcon.Visibility = Visibility.Visible;
            PreviewRestoreButton.IsEnabled = true;
        }
    }

    private const int GwlpHwndParent = -8;

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr newValue);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);
}
