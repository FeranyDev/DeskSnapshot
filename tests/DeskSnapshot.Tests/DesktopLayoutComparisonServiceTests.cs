using System.Diagnostics;
using DeskSnapshot.Models;
using DeskSnapshot.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DeskSnapshot.Tests;

[TestClass]
public sealed class DesktopLayoutComparisonServiceTests
{
    [TestMethod]
    public void Compare_EmptyLayouts_ReturnsNoDifferences()
    {
        var result = DesktopLayoutComparisonService.Compare(TestLayoutFactory.Layout(), TestLayoutFactory.Layout());

        Assert.AreEqual(0, result.Differences.Count);
        Assert.IsFalse(result.EnvironmentChanged);
    }

    [TestMethod]
    public void Compare_PositionsWithinTolerance_AreUnchanged()
    {
        var backup = TestLayoutFactory.Layout([TestLayoutFactory.Icon("Icon", 100, 100)]);
        var current = TestLayoutFactory.Layout([TestLayoutFactory.Icon("Icon", 102, 98)]);

        var result = DesktopLayoutComparisonService.Compare(backup, current);

        Assert.AreEqual(1, result.UnchangedCount);
        Assert.AreEqual(0, result.MovedCount);
    }

    [TestMethod]
    public void Compare_PositionBeyondTolerance_IsMoved()
    {
        var backup = TestLayoutFactory.Layout([TestLayoutFactory.Icon("Icon", 100, 100)]);
        var current = TestLayoutFactory.Layout([TestLayoutFactory.Icon("Icon", 103, 100)]);

        var result = DesktopLayoutComparisonService.Compare(backup, current);

        Assert.AreEqual(1, result.MovedCount);
    }

    [TestMethod]
    public void Compare_ClassifiesMovedAddedMissingAndUnchanged()
    {
        var backup = TestLayoutFactory.Layout(
        [
            TestLayoutFactory.Icon("Unchanged", 10, 10),
            TestLayoutFactory.Icon("Moved", 20, 20),
            TestLayoutFactory.Icon("Missing", 30, 30)
        ]);
        var current = TestLayoutFactory.Layout(
        [
            TestLayoutFactory.Icon("Unchanged", 10, 10),
            TestLayoutFactory.Icon("Moved", 120, 120),
            TestLayoutFactory.Icon("Added", 40, 40)
        ]);

        var result = DesktopLayoutComparisonService.Compare(backup, current);

        Assert.AreEqual(1, result.UnchangedCount);
        Assert.AreEqual(1, result.MovedCount);
        Assert.AreEqual(1, result.AddedCount);
        Assert.AreEqual(1, result.MissingCount);
    }

    [TestMethod]
    public void Compare_IconNames_AreCaseInsensitive()
    {
        var backup = TestLayoutFactory.Layout([TestLayoutFactory.Icon("Readme.txt", 10, 10)]);
        var current = TestLayoutFactory.Layout([TestLayoutFactory.Icon("README.TXT", 10, 10)]);

        var result = DesktopLayoutComparisonService.Compare(backup, current);

        Assert.AreEqual(1, result.UnchangedCount);
        Assert.AreEqual(0, result.AddedCount);
        Assert.AreEqual(0, result.MissingCount);
    }

    [TestMethod]
    public void Compare_DuplicateNames_PairsByCaptureOrderAndMarksAmbiguous()
    {
        var backup = TestLayoutFactory.Layout(
        [
            TestLayoutFactory.Icon("Duplicate", 10, 10, 2),
            TestLayoutFactory.Icon("Duplicate", 20, 20, 1)
        ]);
        var current = TestLayoutFactory.Layout(
        [
            TestLayoutFactory.Icon("Duplicate", 30, 30, 2),
            TestLayoutFactory.Icon("Duplicate", 20, 20, 1)
        ]);

        var result = DesktopLayoutComparisonService.Compare(backup, current);

        Assert.AreEqual(1, result.UnchangedCount);
        Assert.AreEqual(1, result.MovedCount);
        Assert.AreEqual(2, result.AmbiguousCount);
        Assert.IsTrue(result.Differences.All(item => item.IsAmbiguous));
    }

    [TestMethod]
    public void Compare_UnequalDuplicateCounts_ReportsUnpairedItemAsMissing()
    {
        var backup = TestLayoutFactory.Layout(
        [
            TestLayoutFactory.Icon("Duplicate", 10, 10, 0),
            TestLayoutFactory.Icon("Duplicate", 20, 20, 1)
        ]);
        var current = TestLayoutFactory.Layout([TestLayoutFactory.Icon("Duplicate", 10, 10, 0)]);

        var result = DesktopLayoutComparisonService.Compare(backup, current);

        Assert.AreEqual(1, result.UnchangedCount);
        Assert.AreEqual(1, result.MissingCount);
        Assert.AreEqual(2, result.AmbiguousCount);
    }

    [TestMethod]
    public void Compare_DetectsVirtualSizeDpiMonitorCountAndLayoutChanges()
    {
        var backup = TestLayoutFactory.Layout(
            width: 3840,
            dpi: 144,
            monitors: [TestLayoutFactory.Monitor("A", 0, 0, 3840, 2160, true)]);
        var current = TestLayoutFactory.Layout(
            width: 7680,
            dpi: 96,
            monitors:
            [
                TestLayoutFactory.Monitor("A", 3840, 0, 3840, 2160, true),
                TestLayoutFactory.Monitor("B", 0, 0, 3840, 2160)
            ]);

        var result = DesktopLayoutComparisonService.Compare(backup, current);

        Assert.IsTrue(result.VirtualSizeChanged);
        Assert.IsTrue(result.DpiChanged);
        Assert.IsTrue(result.MonitorCountChanged);
        Assert.IsTrue(result.MonitorLayoutChanged);
        Assert.IsTrue(result.EnvironmentChanged);
    }

    [TestMethod]
    public void Compare_MonitorEnumerationOrderDoesNotCreateDifference()
    {
        var monitorA = TestLayoutFactory.Monitor("A", 0, 0, 1920, 1080, true);
        var monitorB = TestLayoutFactory.Monitor("B", 1920, 0, 1920, 1080);
        var backup = TestLayoutFactory.Layout(width: 3840, height: 1080, monitors: [monitorA, monitorB]);
        var current = TestLayoutFactory.Layout(width: 3840, height: 1080, monitors: [monitorB, monitorA]);

        var result = DesktopLayoutComparisonService.Compare(backup, current);

        Assert.IsFalse(result.MonitorLayoutChanged);
    }

    [TestMethod]
    public void Compare_LegacyLayoutsWithoutMonitorMetadata_UsesVirtualOrigin()
    {
        var backup = TestLayoutFactory.Layout(monitors: []);
        backup.Environment.MonitorCount = 1;
        backup.Environment.VirtualLeft = -1920;
        var current = TestLayoutFactory.Layout(monitors: []);
        current.Environment.MonitorCount = 1;

        var result = DesktopLayoutComparisonService.Compare(backup, current);

        Assert.IsTrue(result.MonitorLayoutChanged);
    }

    [TestMethod]
    [Timeout(5000)]
    public void Compare_TwentyFiveThousandIcons_CompletesWithinBudget()
    {
        const int iconCount = 25_000;
        var backupIcons = new List<DesktopIconPosition>(iconCount);
        var currentIcons = new List<DesktopIconPosition>(iconCount);
        for (var index = 0; index < iconCount; index++)
        {
            var x = index % 500 * 12;
            var y = index / 500 * 12;
            backupIcons.Add(TestLayoutFactory.Icon($"Icon {index}", x, y, index));
            currentIcons.Add(TestLayoutFactory.Icon($"Icon {index}", x + (index % 10 == 0 ? 8 : 0), y, index));
        }

        var stopwatch = Stopwatch.StartNew();
        var result = DesktopLayoutComparisonService.Compare(
            TestLayoutFactory.Layout(backupIcons, width: 7680, height: 4320),
            TestLayoutFactory.Layout(currentIcons, width: 7680, height: 4320));
        stopwatch.Stop();

        Assert.AreEqual(iconCount, result.Differences.Count);
        Assert.AreEqual(iconCount / 10, result.MovedCount);
        Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"Comparison took {stopwatch.ElapsedMilliseconds}ms.");
    }
}
