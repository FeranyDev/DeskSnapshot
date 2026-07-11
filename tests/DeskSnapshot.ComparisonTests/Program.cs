using System.Diagnostics;
using DeskSnapshot.Models;
using DeskSnapshot.Services;

RunBehaviorTests();
RunPerformanceTest();
Console.WriteLine("Desktop layout comparison tests passed.");

static void RunBehaviorTests()
{
    var backup = CreateLayout(
        3840,
        2160,
        144,
        [
            Icon("Unchanged", 10, 10, 0),
            Icon("Moved", 20, 20, 1),
            Icon("Missing", 30, 30, 2),
            Icon("Duplicate", 40, 40, 3),
            Icon("Duplicate", 50, 50, 4)
        ]);
    var current = CreateLayout(
        7680,
        2160,
        96,
        [
            Icon("Unchanged", 11, 11, 0),
            Icon("Moved", 120, 120, 1),
            Icon("Added", 60, 60, 2),
            Icon("Duplicate", 40, 40, 3),
            Icon("Duplicate", 80, 80, 4)
        ]);

    var result = DesktopLayoutComparisonService.Compare(backup, current);
    Assert(result.MovedCount == 2, $"Expected 2 moved icons, got {result.MovedCount}.");
    Assert(result.AddedCount == 1, $"Expected 1 added icon, got {result.AddedCount}.");
    Assert(result.MissingCount == 1, $"Expected 1 missing icon, got {result.MissingCount}.");
    Assert(result.UnchangedCount == 2, $"Expected 2 unchanged icons, got {result.UnchangedCount}.");
    Assert(result.AmbiguousCount == 2, $"Expected 2 ambiguous duplicate matches, got {result.AmbiguousCount}.");
    Assert(result.VirtualSizeChanged, "Expected virtual desktop size difference.");
    Assert(result.DpiChanged, "Expected DPI difference.");
    Assert(result.MonitorLayoutChanged, "Expected monitor layout difference.");
}

static void RunPerformanceTest()
{
    const int iconCount = 25_000;
    var backupIcons = new List<DesktopIconPosition>(iconCount);
    var currentIcons = new List<DesktopIconPosition>(iconCount);
    for (var index = 0; index < iconCount; index++)
    {
        var x = index % 500 * 12;
        var y = index / 500 * 12;
        backupIcons.Add(Icon($"Icon {index}", x, y, index));
        currentIcons.Add(Icon($"Icon {index}", x + (index % 10 == 0 ? 8 : 0), y, index));
    }

    var backup = CreateLayout(7680, 4320, 144, backupIcons);
    var current = CreateLayout(7680, 4320, 144, currentIcons);
    var stopwatch = Stopwatch.StartNew();
    var result = DesktopLayoutComparisonService.Compare(backup, current);
    stopwatch.Stop();

    Assert(result.Differences.Count == iconCount, "Large comparison lost icon records.");
    Assert(result.MovedCount == iconCount / 10, "Large comparison returned an incorrect moved count.");
    Assert(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"Large comparison took {stopwatch.ElapsedMilliseconds}ms.");
    Console.WriteLine($"Compared {iconCount:N0} icons in {stopwatch.ElapsedMilliseconds}ms.");
}

static DesktopLayoutBackup CreateLayout(int width, int height, uint dpi, List<DesktopIconPosition> icons)
{
    var monitors = new List<DesktopMonitor>
    {
        new()
        {
            Id = "DISPLAY-A",
            Name = "Test monitor A",
            Width = Math.Min(width, 3840),
            Height = height,
            IsPrimary = true
        }
    };
    if (width > 3840)
    {
        monitors.Add(new DesktopMonitor
        {
            Id = "DISPLAY-B",
            Name = "Test monitor B",
            Left = 3840,
            Width = width - 3840,
            Height = height
        });
    }

    return new DesktopLayoutBackup
    {
        Environment = new DesktopEnvironment
        {
            VirtualWidth = width,
            VirtualHeight = height,
            Dpi = dpi,
            MonitorCount = monitors.Count,
            Monitors = monitors
        },
        Icons = icons
    };
}

static DesktopIconPosition Icon(string name, int x, int y, int order) => new()
{
    Name = name,
    X = x,
    Y = y,
    CaptureOrder = order
};

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
