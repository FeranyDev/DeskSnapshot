namespace DeskSnapshot.Services;

public static class StartupLaunchDetector
{
    public const string StartupArgument = "--startup";

    public static bool IsStartupLaunch(IEnumerable<string> commandLineArguments, bool activatedByStartupTask) =>
        activatedByStartupTask || commandLineArguments.Any(argument =>
            string.Equals(argument, StartupArgument, StringComparison.OrdinalIgnoreCase));
}
