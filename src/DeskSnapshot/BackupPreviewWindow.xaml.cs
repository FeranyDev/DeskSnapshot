using DeskSnapshot.Models;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace DeskSnapshot;

public sealed partial class BackupPreviewWindow : Window
{
    private const double CanvasWidth = 1200;
    private const double IconSize = 20;
    private const double Inset = 14;

    public BackupPreviewWindow(DesktopLayoutBackup backup)
    {
        InitializeComponent();
        Title = $"{backup.Name} - 布局预览";

        try
        {
            SystemBackdrop = new MicaBackdrop();
        }
        catch
        {
            // Mica 不可用时保留默认背景。
        }

        ConfigureWindowSize();
        PopulateHeader(backup);
        DrawLayout(backup);
    }

    private void ConfigureWindowSize()
    {
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(handle);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        var workArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary).WorkArea;

        var width = Math.Min(1800, (int)(workArea.Width * 0.84));
        var height = Math.Min(1100, (int)(workArea.Height * 0.82));
        var x = workArea.X + (workArea.Width - width) / 2;
        var y = workArea.Y + (workArea.Height - height) / 2;
        appWindow.MoveAndResize(new RectInt32(x, y, width, height));
    }

    private void PopulateHeader(DesktopLayoutBackup backup)
    {
        var environment = backup.Environment;
        BackupNameText.Text = backup.Name;
        BackupSummaryText.Text = $"{backup.CreatedAt.LocalDateTime:yyyy-MM-dd HH:mm:ss}  ·  {backup.Icons.Count} 个图标  ·  {environment.VirtualWidth} × {environment.VirtualHeight}  ·  DPI {environment.Dpi}  ·  {environment.MonitorCount} 台显示器";
        PreviewHintText.Text = "悬停图标查看名称和坐标";

        if (!string.IsNullOrWhiteSpace(backup.Note))
        {
            BackupNoteText.Text = $"备注：{backup.Note}";
            BackupNoteText.Visibility = Visibility.Visible;
        }
    }

    private void DrawLayout(DesktopLayoutBackup backup)
    {
        var coordinateWidth = Math.Max(1d, backup.Environment.VirtualWidth);
        var coordinateHeight = Math.Max(1d, backup.Environment.VirtualHeight);
        var canvasHeight = Math.Clamp(CanvasWidth * coordinateHeight / coordinateWidth, 420, 820);
        LayoutCanvas.Height = canvasHeight;

        var usableWidth = CanvasWidth - Inset * 2;
        var usableHeight = canvasHeight - Inset * 2;

        foreach (var icon in backup.Icons.OrderBy(item => item.CaptureOrder))
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
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
