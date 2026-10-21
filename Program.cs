using System.Runtime.Versioning;
using Newtonsoft.Json;
using Serilog;
using Wolves.Catalog;

[assembly: SupportedOSPlatform("windows")]

namespace Wolves;

internal class Program
{
    // Folders relative to the executable
    private static readonly string FinalOutputFolder = Path.Combine(AppContext.BaseDirectory, "Extracted");
    private static readonly string DefaultsFolder = Path.Combine(AppContext.BaseDirectory, "Defaults");
    private static readonly string DefaultCustomizationItemsPath = Path.Combine(DefaultsFolder, "DefaultCustomizationItems.json");
    private static readonly string DecryptedCatalogPath = Path.Combine(DefaultsFolder, "Catalog.decrypted.json");
    private static readonly string ErrorLogPath = Path.Combine(AppContext.BaseDirectory, "Wolves.error.log");

    /// <param name="args">Optional: path to the Dead by Daylight installation folder.</param>
    private static async Task<int> Main(string[] args)
    {
        ConsoleWindow.Apply();
        Console.WriteLine($"{AppInfo.Name} v{AppInfo.Version}");
        Console.WriteLine();

        // CUE4Parse reports mount/decompression problems only through Serilog; without a logger they are silently dropped
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Warning()
            // Harmless: DBD containers use "/" as mount point and CUE4Parse mounts them to root anyway
            .Filter.ByExcluding(e => e.MessageTemplate.Text.Contains("has strange mount point", StringComparison.Ordinal))
            .WriteTo.Console()
            .CreateLogger();

        // Native DLLs (Oodle) are downloaded/loaded relative to the working directory, which differs when started from a shortcut or another folder
        Directory.SetCurrentDirectory(AppContext.BaseDirectory);

        try
        {
            return await RunAsync(args);
        }
        catch (Exception ex)
        {
            ReportError(ex);
            return 1;
        }
        finally
        {
            await Log.CloseAndFlushAsync();
            WaitForKey();
        }
    }

    private static async Task<int> RunAsync(string[] args)
    {
        Directory.CreateDirectory(FinalOutputFolder);

        // Step 1: Ask the user for the .usmap file and copy it next to the other outputs
        Console.WriteLine("[1/5] Select the mappings (.usmap) file...");
        string? selectedUsmap = FileOpenDialog.ShowDialog(
            title: "Select the Dead by Daylight mappings file",
            filterName: "Unreal mappings (*.usmap)",
            filterSpec: "*.usmap",
            defaultExtension: "usmap",
            initialDirectory: FinalOutputFolder);

        if (selectedUsmap is null)
        {
            Console.WriteLine("      No file selected. Nothing to do.");
            return 1;
        }

        string finalUsmapPath = StageMappingsFile(selectedUsmap, FinalOutputFolder);
        Console.WriteLine($"      Mappings ready at: {finalUsmapPath}");
        Extras.Settings.MappingsPath = finalUsmapPath;

        // Step 2: Mount the game with CUE4Parse
        Console.WriteLine("[2/5] Initializing CUE4Parse...");
        string? pakPath = GameLocator.ResolvePakPath(args.FirstOrDefault());
        if (pakPath is null)
        {
            Console.WriteLine("      No game folder selected. Nothing to do.");
            return 1;
        }

        Extras.Settings.PakPath = pakPath;
        Console.WriteLine($"      Paks: {pakPath}");
        Cue4Parse.Initialize();

        // Step 3: Extract the DB assets
        Console.WriteLine("[3/5] Extracting DB assets...");
        Cue4Parse.Get_Files();

        Console.WriteLine($"      Extracted Items:      {Classes.Ids.ItemIds.Count}");
        Console.WriteLine($"      Extracted Addons:     {Classes.Ids.ItemAddonIds.Count}");
        Console.WriteLine($"      Extracted Offerings:  {Classes.Ids.OfferingIds.Count}");
        Console.WriteLine($"      Extracted Perks:      {Classes.Ids.PerkIds.Count}");
        Console.WriteLine($"      Extracted Characters: {Classes.Ids.CharacterIds.Count}");

        await WriteJsonAsync(Path.Combine(FinalOutputFolder, "Items.json"), Classes.Ids.ItemIds);
        await WriteJsonAsync(Path.Combine(FinalOutputFolder, "Addons.json"), Classes.Ids.ItemAddonIds);
        await WriteJsonAsync(Path.Combine(FinalOutputFolder, "Offerings.json"), Classes.Ids.OfferingIds);
        await WriteJsonAsync(Path.Combine(FinalOutputFolder, "Perks.json"), Classes.Ids.PerkIds);
        await WriteJsonAsync(Path.Combine(FinalOutputFolder, "Characters.json"), Classes.Ids.CharacterIds);
        Console.WriteLine($"      Saved data to: {FinalOutputFolder}");

        // Step 4: Generate Bloodweb.json, GetAll.json, ItemsList.json and CharactersList.json from the extracted data
        Console.WriteLine("[4/5] Generating Bloodweb.json, GetAll.json, ItemsList.json and CharactersList.json...");
        await CatalogGenerator.WriteBloodWebAsync(Path.Combine(FinalOutputFolder, "Bloodweb.json"));
        await CatalogGenerator.WriteGetAllAsync(Path.Combine(FinalOutputFolder, "GetAll.json"));
        await CatalogGenerator.WriteItemsListAsync(Path.Combine(FinalOutputFolder, "ItemsList.json"));
        await CatalogGenerator.WriteCharactersListAsync(Path.Combine(FinalOutputFolder, "CharactersList.json"));

        Console.WriteLine($"      Bloodweb.json: {CatalogGenerator.BuildCharacterItems().Count} inventory entries");
        Console.WriteLine($"      GetAll.json:   {Classes.Ids.CharacterIds.Count} characters");
        Console.WriteLine($"      ItemsList.json: {Classes.Ids.PerkIds.Count} perks, {Classes.Ids.ItemIds.Count} items, {Classes.Ids.ItemAddonIds.Count} addons, {Classes.Ids.OfferingIds.Count} offerings");
        Console.WriteLine($"      CharactersList.json: {CatalogGenerator.CharacterIdsInOrder().Count} characters");

        // Step 5: Generate the market files from the default customization items + bundled decrypted catalog
        Console.WriteLine("[5/5] Generating MarketFull.json, MarketDLC.json and CatalogUnlocked.json...");
        var defaultCustomizationIds = CatalogGenerator.LoadDefaultCustomizationItems(DefaultCustomizationItemsPath);
        if (defaultCustomizationIds.Count == 0)
            Console.WriteLine($"      [WARN] No default customization items found at '{DefaultCustomizationItemsPath}'.");

        if (!File.Exists(DecryptedCatalogPath))
            throw new FileNotFoundException($"Decrypted catalog not found at '{DecryptedCatalogPath}'.");

        var catalogIds = CatalogGenerator.LoadCatalogIds(DecryptedCatalogPath);

        await CatalogGenerator.WriteMarketFullAsync(Path.Combine(FinalOutputFolder, "MarketFull.json"), defaultCustomizationIds, catalogIds);
        await CatalogGenerator.WriteMarketDlcAsync(Path.Combine(FinalOutputFolder, "MarketDLC.json"), defaultCustomizationIds);
        var unlockOptions = new CatalogGenerator.UnlockOptions(
            ReplaceDates: true,
            Unbreakable: CatalogGenerator.UnbreakableMode.FalseCompact);
        var unlock = CatalogGenerator.WriteUnlockedCatalog(Path.Combine(FinalOutputFolder, "CatalogUnlocked.json"), DecryptedCatalogPath, unlockOptions);

        Console.WriteLine($"      MarketFull.json: {defaultCustomizationIds.Count} defaults + {catalogIds.Count} catalog entries (characters and bundles excluded)");
        Console.WriteLine($"      MarketDLC.json:  {defaultCustomizationIds.Count} defaults + {CatalogGenerator.CharacterIdsInOrder().Count} characters (bundles excluded)");
        Console.WriteLine($"      CatalogUnlocked.json: {unlock.DatesReplaced} dates replaced ({unlock.DatesSkipped} skipped), " +
                          $"{unlock.UnbreakableReplaced} unbreakable -> {unlockOptions.Unbreakable} ({unlock.UnbreakableSkipped} skipped), " +
                          (unlock.SizeDelta == 0 ? $"size unchanged ({unlock.Size:N0} bytes)" : $"size {unlock.Size:N0} bytes ({unlock.SizeDelta:+#;-#} bytes)"));
        Console.WriteLine($"      Saved to: {FinalOutputFolder}");

        return 0;
    }

    /// <summary>
    /// Prints every exception in the chain (the outer message alone usually hides the real cause)
    /// and saves the full stack trace next to the exe.
    /// </summary>
    private static void ReportError(Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine();
        Console.Error.WriteLine($"[ERROR] {ex.GetType().Name}: {ex.Message}");

        for (Exception? inner = ex.InnerException; inner is not null; inner = inner.InnerException)
            Console.Error.WriteLine($"  Caused by {inner.GetType().Name}: {inner.Message}");

        if (ex is FileNotFoundException { FileName: { } missingFile })
            Console.Error.WriteLine($"  Missing file: {missingFile}");

        Console.ResetColor();

        try
        {
            File.WriteAllText(ErrorLogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}{ex}");
            Console.Error.WriteLine($"  Full details saved to: {ErrorLogPath}");
        }
        catch (Exception)
        {
            Console.Error.WriteLine(ex.ToString());
        }
    }

    // Keeps the window open when the exe is started by double-click
    private static void WaitForKey()
    {
        if (Console.IsInputRedirected || Console.IsOutputRedirected)
            return;

        Console.WriteLine();
        Console.WriteLine("Press any key to exit...");
        Console.ReadKey(intercept: true);
    }

    /// <summary>
    /// Copies the user-selected mappings file into the output folder as <c>mappings.usmap</c>.
    /// The original file is left untouched.
    /// </summary>
    private static string StageMappingsFile(string selectedPath, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);
        string destination = Path.Combine(destinationDir, "mappings.usmap");

        bool sameFile = string.Equals(
            Path.GetFullPath(selectedPath),
            Path.GetFullPath(destination),
            StringComparison.OrdinalIgnoreCase);

        if (!sameFile)
            File.Copy(selectedPath, destination, overwrite: true);

        return destination;
    }

    private static Task WriteJsonAsync(string path, object value) =>
        File.WriteAllTextAsync(path, JsonConvert.SerializeObject(value, Formatting.Indented));
}
