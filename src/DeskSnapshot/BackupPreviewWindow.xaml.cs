using DeskSnapshot.Models;
using DeskSnapshot.Services;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
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

    public BackupPreviewWindow(DesktopLayoutBackup backup, MainWindow owner)
    {
        _backup = backup;
        _owner = owner;
        InitializeComponent();
        LocalizationBindings.Apply(RootGrid);
        RootGrid.Loaded += (_, _) => LocalizationBindings.Apply(RootGrid);
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
        var verticalChrome = ((string.IsNullOrWhiteSpace(backup.Note) ? 215 : 240) + monitorDetailsHeight) * dpiScale;
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

        if (_backup.Icons.Count == 0)
        {
            EmptyPreviewText.Visibility = Visibility.Visible;
            App.Log($"Backup preview opened with no icons: {_backup.Id}");
            return;
        }

        EmptyPreviewText.Visibility = Visibility.Collapsed;
        var coordinateWidth = Math.Max(1d, _backup.Environment.VirtualWidth);
        var coordinateHeight = Math.Max(1d, _backup.Environment.VirtualHeight);
        var usableWidth = canvasWidth - Inset * 2;
        var usableHeight = canvasHeight - Inset * 2;

        foreach (var icon in _backup.Icons.OrderBy(item => item.CaptureOrder))
        {
            var tile = new Border
            {
                Width = IconSize,
                Height = IconSize,
                CornerRadius = new CornerRadius(5),
                Background = new SolidColorBrush(ColorHelper.FromArgb(230, 245, 249, 255)),
                BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(190, 100, 165, 255)),
                BorderThickness = new Thickness(1),
                Child = new FontIcon
                {
                    Glyph = "\uE8B7",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 20, 70, 130))
                }
            };

            ToolTipService.SetToolTip(tile, $"{icon.Name}\nX {icon.X}  ·  Y {icon.Y}");

            var left = Inset + Math.Clamp(icon.X / coordinateWidth * usableWidth, 0, usableWidth - IconSize);
            var top = Inset + Math.Clamp(icon.Y / coordinateHeight * usableHeight, 0, usableHeight - IconSize);
            Canvas.SetLeft(tile, left);
            Canvas.SetTop(tile, top);
            LayoutCanvas.Children.Add(tile);
        }

        App.Log($"Backup preview rendered {LayoutCanvas.Children.Count} square icons for {_backup.Id} at {canvasWidth:F0}x{canvasHeight:F0}");
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
