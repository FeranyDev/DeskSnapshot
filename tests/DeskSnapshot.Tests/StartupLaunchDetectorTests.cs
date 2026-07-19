using DeskSnapshot.Services;

namespace DeskSnapshot.Tests;

[TestClass]
public sealed class StartupLaunchDetectorTests
{
    [TestMethod]
    public void IsStartupLaunch_ReturnsTrueForStartupTaskActivation()
    {
        Assert.IsTrue(StartupLaunchDetector.IsStartupLaunch([], activatedByStartupTask: true));
    }

    [TestMethod]
    public void IsStartupLaunch_ReturnsTrueForPortableStartupArgument()
    {
        Assert.IsTrue(StartupLaunchDetector.IsStartupLaunch(
            ["DeskSnapshot.exe", "--STARTUP"],
            activatedByStartupTask: false));
    }

    [TestMethod]
    public void IsStartupLaunch_ReturnsFalseForNormalLaunch()
    {
        Assert.IsFalse(StartupLaunchDetector.IsStartupLaunch(
            ["DeskSnapshot.exe"],
            activatedByStartupTask: false));
    }
}
