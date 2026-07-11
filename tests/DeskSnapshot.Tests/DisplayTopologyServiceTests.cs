using DeskSnapshot.Models;
using DeskSnapshot.Services;

namespace DeskSnapshot.Tests;

[TestClass]
public sealed class DisplayTopologyServiceTests
{
    [TestMethod]
    public void CreateFingerprint_IgnoresMonitorEnumerationOrder()
    {
        var left = TestLayoutFactory.Monitor("LEFT", -1920, 0, 1920, 1080);
        var primary = TestLayoutFactory.Monitor("MAIN", 0, 0, 3840, 2160, true);
        var first = Environment([left, primary], 144);
        var second = Environment([primary, left], 144);

        Assert.AreEqual(
            DisplayTopologyService.CreateFingerprint(first),
            DisplayTopologyService.CreateFingerprint(second));
    }

    [TestMethod]
    public void CreateFingerprint_IgnoresAbsoluteVirtualDesktopOrigin()
    {
        var first = Environment([
            TestLayoutFactory.Monitor("MAIN", 0, 0, 1920, 1080, true),
            TestLayoutFactory.Monitor("SIDE", 1920, 0, 1920, 1080)]);
        var shifted = Environment([
            TestLayoutFactory.Monitor("MAIN", -1920, -1080, 1920, 1080, true),
            TestLayoutFactory.Monitor("SIDE", 0, -1080, 1920, 1080)]);

        Assert.AreEqual(
            DisplayTopologyService.CreateFingerprint(first),
            DisplayTopologyService.CreateFingerprint(shifted));
    }

    [TestMethod]
    [DataRow("position")]
    [DataRow("rotation")]
    [DataRow("primary")]
    [DataRow("device")]
    [DataRow("dpi")]
    public void CreateFingerprint_DetectsMeaningfulDisplayChanges(string change)
    {
        var baseline = Environment([
            TestLayoutFactory.Monitor("MAIN", 0, 0, 1920, 1080, true),
            TestLayoutFactory.Monitor("SIDE", 1920, 0, 2560, 1440)]);
        var changed = change switch
        {
            "position" => Environment([
                TestLayoutFactory.Monitor("MAIN", 0, 0, 1920, 1080, true),
                TestLayoutFactory.Monitor("SIDE", -2560, 0, 2560, 1440)]),
            "rotation" => Environment([
                TestLayoutFactory.Monitor("MAIN", 0, 0, 1920, 1080, true),
                TestLayoutFactory.Monitor("SIDE", 1920, 0, 1440, 2560)]),
            "primary" => Environment([
                TestLayoutFactory.Monitor("MAIN", 0, 0, 1920, 1080),
                TestLayoutFactory.Monitor("SIDE", 1920, 0, 2560, 1440, true)]),
            "device" => Environment([
                TestLayoutFactory.Monitor("MAIN", 0, 0, 1920, 1080, true),
                TestLayoutFactory.Monitor("OTHER", 1920, 0, 2560, 1440)]),
            "dpi" => Environment([
                TestLayoutFactory.Monitor("MAIN", 0, 0, 1920, 1080, true),
                TestLayoutFactory.Monitor("SIDE", 1920, 0, 2560, 1440)], 120),
            _ => throw new ArgumentOutOfRangeException(nameof(change))
        };

        Assert.AreNotEqual(
            DisplayTopologyService.CreateFingerprint(baseline),
            DisplayTopologyService.CreateFingerprint(changed));
    }

    [TestMethod]
    public void Matches_RejectsEmptyAndAcceptsGeneratedFingerprint()
    {
        var environment = Environment([TestLayoutFactory.Monitor("MAIN", 0, 0, 1920, 1080, true)]);
        var fingerprint = DisplayTopologyService.CreateFingerprint(environment);

        Assert.IsFalse(DisplayTopologyService.Matches(environment, null));
        Assert.IsTrue(DisplayTopologyService.Matches(environment, fingerprint.ToLowerInvariant()));
    }

    private static DesktopEnvironment Environment(List<DesktopMonitor> monitors, uint dpi = 144) => new()
    {
        Dpi = dpi,
        MonitorCount = monitors.Count,
        Monitors = monitors,
        VirtualWidth = monitors.Max(monitor => monitor.Left + monitor.Width) - monitors.Min(monitor => monitor.Left),
        VirtualHeight = monitors.Max(monitor => monitor.Top + monitor.Height) - monitors.Min(monitor => monitor.Top)
    };
}
