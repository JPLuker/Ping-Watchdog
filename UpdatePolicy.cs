using Microsoft.Win32;

namespace PingWatchdog;

// DATA-AND-UPDATES 3.2/3.3: at most one automatic check a day, and IT can turn updating off.
internal static class UpdatePolicy
{
    internal static readonly TimeSpan MinimumInterval = TimeSpan.FromDays(1);
    internal const string PolicyKey = @"Software\Policies\PingWatchdog";
    internal const string PolicyValue = "DisableUpdates";
    internal const string EnvironmentVariable = "PINGWATCHDOG_DISABLE_UPDATES";
    internal const string ManagedText = "Updates are managed by your organization";

    // Due when never checked, a day has passed, or the clock moved backwards past the last check.
    internal static bool IsDue(DateTimeOffset? lastCheck, DateTimeOffset now) =>
        lastCheck is not { } last || now - last >= MinimumInterval || last - now > TimeSpan.FromHours(1);

    internal static bool DisabledByPolicy => Evaluate(ReadPolicyValue(), Environment.GetEnvironmentVariable(EnvironmentVariable));

    internal static bool Evaluate(object? registryValue, string? environment) =>
        registryValue switch
        {
            int number => number != 0,
            long number => number != 0,
            string text => IsTrue(text),
            _ => false
        } || IsTrue(environment);

    private static bool IsTrue(string? value) =>
        value?.Trim() is { } text && (text == "1" || text.Equals("true", StringComparison.OrdinalIgnoreCase) || text.Equals("yes", StringComparison.OrdinalIgnoreCase));

    private static object? ReadPolicyValue()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(PolicyKey);
            return key?.GetValue(PolicyValue);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return null; }
    }
}
