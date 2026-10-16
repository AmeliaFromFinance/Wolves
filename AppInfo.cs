using System.Reflection;

namespace Wolves;

/// <summary>
/// Program name and version, taken from &lt;Version&gt; in Wolves.csproj.
/// </summary>
internal static class AppInfo
{
    public const string Name = "Wolves";

    /// <summary>Product version, e.g. "1.1.0".</summary>
    public static string Version { get; } =
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(AppInfo).Assembly.GetName().Version?.ToString(3)
        ?? "?";
}
