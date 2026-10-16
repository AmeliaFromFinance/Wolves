using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Wolves;

/// <summary>
/// Finds the Dead by Daylight <c>Paks</c> folder on the current machine instead of relying on a hardcoded path.
/// Order: command-line argument, saved settings, Steam libraries, Epic Games installs, folder picker.
/// </summary>
[SupportedOSPlatform("windows")]
public static partial class GameLocator
{
    private const string SteamGameFolder = "Dead by Daylight";
    private const string GameDisplayName = "Dead by Daylight";
    private const string GameProjectFolder = "DeadByDaylight";
    private static readonly string SettingsPath = Path.Combine(AppContext.BaseDirectory, "Wolves.settings.json");

    private sealed class SavedSettings
    {
        public string? GameInstallDir { get; set; }
    }

    /// <summary>
    /// Returns the full path to the game's Paks folder, or <c>null</c> if the user cancelled the folder picker.
    /// </summary>
    public static string? ResolvePakPath(string? commandLinePath)
    {
        // 1. Explicit path passed on the command line (or by dropping a folder onto the exe)
        if (!string.IsNullOrWhiteSpace(commandLinePath))
        {
            string? fromArgs = FindPaks(commandLinePath);
            if (fromArgs is null)
                throw new DirectoryNotFoundException($"No Dead by Daylight .pak/.utoc files found in '{commandLinePath}' (passed on the command line).");

            Console.WriteLine($"      Using game path from command line: {commandLinePath}");
            Save(commandLinePath);
            return fromArgs;
        }

        // 2. Path remembered from a previous run
        string? saved = LoadSaved();
        if (saved is not null && FindPaks(saved) is { } fromSettings)
        {
            Console.WriteLine($"      Using saved game path: {saved}");
            return fromSettings;
        }

        // 3. Launchers: Steam, then Epic Games
        var launchers = new (string Name, Func<IEnumerable<string>> Dirs)[]
        {
            ("Steam", new Func<IEnumerable<string>>(SteamGameDirs)),
            ("Epic Games", new Func<IEnumerable<string>>(EpicGameDirs))
        };

        foreach (var (name, dirs) in launchers)
        {
            foreach (string candidate in SafeEnumerate(dirs))
            {
                if (FindPaks(candidate) is { } fromLauncher)
                {
                    Console.WriteLine($"      Found game in {name}: {candidate}");
                    Save(candidate);
                    return fromLauncher;
                }
            }
        }

        // 4. Ask the user
        Console.WriteLine("      Game not found automatically. Select the Dead by Daylight installation folder...");
        while (true)
        {
            string? picked = FileOpenDialog.ShowFolderDialog("Select the Dead by Daylight installation folder");
            if (picked is null)
                return null;

            if (FindPaks(picked) is { } fromPicker)
            {
                Save(picked);
                return fromPicker;
            }

            Console.WriteLine($"      [WARN] No .pak/.utoc files found under '{picked}'. Pick the folder that contains 'DeadByDaylight' (or the 'Paks' folder itself).");
        }
    }

    /// <summary>
    /// Accepts the game root, the inner "DeadByDaylight" folder or the Paks folder itself.
    /// </summary>
    private static string? FindPaks(string dir)
    {
        if (!Directory.Exists(dir))
            return null;

        string[] candidates =
        [
            Path.Combine(dir, GameProjectFolder, "Content", "Paks"),
            Path.Combine(dir, "Content", "Paks"),
            dir
        ];

        return candidates.FirstOrDefault(ContainsArchives);
    }

    // Top level only: the game keeps its archives directly in Paks, and a deep scan of a drive root would be slow
    private static bool ContainsArchives(string dir) =>
        Directory.Exists(dir) &&
        (Directory.EnumerateFiles(dir, "*.pak").Any() || Directory.EnumerateFiles(dir, "*.utoc").Any());

    private static IEnumerable<string> SteamGameDirs()
    {
        var steamRoots = new List<string>();

        AddRegistryPath(steamRoots, Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath");
        AddRegistryPath(steamRoots, Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath");
        AddRegistryPath(steamRoots, Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath");

        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string root in steamRoots)
        {
            libraries.Add(root);

            // libraryfolders.vdf lists every Steam library on every drive
            string vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf))
                continue;

            try
            {
                foreach (Match m in VdfPathRegex().Matches(File.ReadAllText(vdf)))
                    libraries.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        return libraries.Select(lib => Path.Combine(lib, "steamapps", "common", SteamGameFolder));
    }

    /// <summary>
    /// Epic keeps one JSON manifest (*.item) per installed game, plus a combined LauncherInstalled.dat.
    /// </summary>
    private static IEnumerable<string> EpicGameDirs()
    {
        var dataDirs = new List<string>();
        AddRegistryPath(dataDirs, Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Epic Games\EpicGamesLauncher", "AppDataPath");
        AddRegistryPath(dataDirs, Registry.LocalMachine, @"SOFTWARE\Epic Games\EpicGamesLauncher", "AppDataPath");

        string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        dataDirs.Add(Path.Combine(programData, "Epic", "EpicGamesLauncher", "Data"));

        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string manifestDir in dataDirs.Select(d => Path.Combine(d, "Manifests")).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(manifestDir))
                continue;

            foreach (string item in Directory.EnumerateFiles(manifestDir, "*.item"))
            {
                JObject? manifest = TryReadJson(item);
                string? displayName = manifest?["DisplayName"]?.ToString();
                string? location = manifest?["InstallLocation"]?.ToString();

                if (location is not null && displayName?.Contains(GameDisplayName, StringComparison.OrdinalIgnoreCase) == true)
                    results.Add(location);
            }
        }

        // No display names here, so only accept folders that look like the game
        string installedDat = Path.Combine(programData, "Epic", "UnrealEngineLauncher", "LauncherInstalled.dat");
        if (TryReadJson(installedDat)?["InstallationList"] is JArray installs)
        {
            foreach (var install in installs)
            {
                string? location = install["InstallLocation"]?.ToString();
                if (location is not null && Directory.Exists(Path.Combine(location, GameProjectFolder, "Content", "Paks")))
                    results.Add(location);
            }
        }

        return results;
    }

    private static JObject? TryReadJson(string path)
    {
        try
        {
            return File.Exists(path) ? JObject.Parse(File.ReadAllText(path)) : null;
        }
        catch (Exception)
        {
            return null; // corrupted or locked launcher file
        }
    }

    // One broken launcher install must not stop detection in the others
    private static List<string> SafeEnumerate(Func<IEnumerable<string>> source)
    {
        try
        {
            return source().ToList();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"      [WARN] Game search failed: {ex.Message}");
            return [];
        }
    }

    private static void AddRegistryPath(List<string> list, RegistryKey hive, string subKey, string valueName)
    {
        try
        {
            using var key = hive.OpenSubKey(subKey);
            if (key?.GetValue(valueName) is string path && !string.IsNullOrWhiteSpace(path))
                list.Add(Path.GetFullPath(path.Replace('/', '\\')));
        }
        catch (Exception) { /* registry access is best effort */ }
    }

    private static string? LoadSaved()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return null;

            return JsonConvert.DeserializeObject<SavedSettings>(File.ReadAllText(SettingsPath))?.GameInstallDir;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"      [WARN] Could not read '{SettingsPath}': {ex.Message}");
            return null;
        }
    }

    private static void Save(string gameInstallDir)
    {
        try
        {
            var settings = new SavedSettings { GameInstallDir = gameInstallDir };
            File.WriteAllText(SettingsPath, JsonConvert.SerializeObject(settings, Formatting.Indented));
        }
        catch (Exception ex)
        {
            // Not fatal: the path will just be detected again next time
            Console.WriteLine($"      [WARN] Could not save game path to '{SettingsPath}': {ex.Message}");
        }
    }

    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex VdfPathRegex();
}
