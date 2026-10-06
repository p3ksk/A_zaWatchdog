using System.Reflection;

namespace AlzaWatchdog.Api;

public static class AppInfo
{
    /// <summary>
    /// The csproj version. The SDK may append "+commit" to the informational
    /// version; that is noise to a reader, so it is cut.
    /// </summary>
    public static readonly string Version =
        typeof(AppInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            .Split('+')[0] ?? "";
}
