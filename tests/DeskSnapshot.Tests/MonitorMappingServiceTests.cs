using DeskSnapshot.Models;
using DeskSnapshot.Services;

namespace DeskSnapshot.Tests;

[TestClass]
public sealed class MonitorMappingServiceTests
{
    [TestMethod]
    public void ResolveTargetMonitor_PrefersExplicitMapping()
    {
        var source = TestLayoutFactory.Monitor("OLD", 0, 0, 1920, 1080, true);
        var expected = TestLayoutFactory.Monitor("TARGET-B", 1920, 0, 1920, 1080);
        var current = Environment([
            TestLayoutFactory.Monitor("TARGET-A", 0, 0, 1920, 1080, true),
            expected]);

        var result = MonitorMappingService.ResolveTargetMonitor(source, current, "target-b");

        Assert.AreSame(expected, result);
    }

    [TestMethod]
    public void ResolveTargetMonitor_FallsBackToStableDeviceId()
    {
        var source = TestLayoutFactory.Monitor("SAME", 0, 0, 1920, 1080, true);
        source.DeviceName = "OLD-GDI-NAME";
        var expected = TestLayoutFactory.Monitor("SAME", 1920, 0, 1920, 1080);
        expected.DeviceName = "NEW-GDI-NAME";

        Assert.AreSame(expected, MonitorMappingService.ResolveTargetMonitor(source, Environment([expected])));
    }

    [TestMethod]
    public void ResolveTargetMonitor_UsesUniqueFriendlyNameOnly()
    {
        var source = TestLayoutFactory.Monitor("MISSING", 0, 0, 1920, 1080, true);
        source.Name = "Studio Display";
        var expected = TestLayoutFactory.Monitor("NEW", 0, 0, 1920, 1080, true);
        expected.Name = "Studio Display";

        Assert.AreSame(expected, MonitorMappingService.ResolveTargetMonitor(source, Environment([expected])));
    }

    [TestMethod]
    public void ResolveTargetMonitor_DoesNotGuessWhenFriendlyNameIsAmbiguous()
    {
        var source = TestLayoutFactory.Monitor("MISSING", 0, 0, 1920, 1080, true);
        source.Name = "Generic PnP Monitor";
        var first = TestLayoutFactory.Monitor("A", 0, 0, 1920, 1080, true);
        var second = TestLayoutFactory.Monitor("B", 1920, 0, 1920, 1080);
        first.Name = second.Name = "Generic PnP Monitor";

        Assert.IsNull(MonitorMappingService.ResolveTargetMonitor(source, Environment([first, second])));
    }

    [TestMethod]
    public void ResolveTargetMonitor_IgnoresRememberedTargetThatIsNoLongerConnected()
    {
        var source = TestLayoutFactory.Monitor("SOURCE", 0, 0, 1920, 1080, true);
        var current = TestLayoutFactory.Monitor("CURRENT", 0, 0, 1920, 1080, true);

        Assert.IsNull(MonitorMappingService.ResolveTargetMonitor(source, Environment([current]), "REMOVED"));
    }

    [TestMethod]
    public void ResolveTargetMonitor_ExplicitEmptyMappingDisablesAutomaticMatch()
    {
        var source = TestLayoutFactory.Monitor("SAME", 0, 0, 1920, 1080, true);
        var current = TestLayoutFactory.Monitor("SAME", 0, 0, 1920, 1080, true);

        Assert.IsNull(MonitorMappingService.ResolveTargetMonitor(source, Environment([current]), string.Empty));
        Assert.AreSame(current, MonitorMappingService.ResolveTargetMonitor(source, Environment([current])));
    }

    [TestMethod]
    public void CreateMappingKey_ChangesWhenEitherTopologyChanges()
    {
        var source = Environment([TestLayoutFactory.Monitor("SOURCE", 0, 0, 1920, 1080, true)]);
        var target = Environment([TestLayoutFactory.Monitor("TARGET", 0, 0, 1920, 1080, true)]);
        var changedTarget = Environment([TestLayoutFactory.Monitor("OTHER", 0, 0, 1920, 1080, true)]);

        Assert.AreNotEqual(
            MonitorMappingService.CreateMappingKey(source, target),
            MonitorMappingService.CreateMappingKey(source, changedTarget));
    }

    private static DesktopEnvironment Environment(List<DesktopMonitor> monitors) => new()
    {
        Dpi = 96,
        MonitorCount = monitors.Count,
        Monitors = monitors,
        VirtualWidth = monitors.Sum(monitor => monitor.Width),
        VirtualHeight = monitors.Max(monitor => monitor.Height)
    };
}
