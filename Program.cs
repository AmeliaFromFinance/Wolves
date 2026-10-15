using System.Runtime.Versioning;
using Newtonsoft.Json;
using Wolves.Catalog;

[assembly: SupportedOSPlatform("windows")]

namespace Wolves;

internal class Program
{
    // Game installation (Dead by Daylight)
    private const string GameInstallDir = @"C:\Program Files (x86)\Steam\steamapps\common\Dead by Daylight";

    // Folders relative to the executable
    private static readonly string FinalOutputFolder = Path.Combine(AppContext.BaseDirectory, "Extracted");
    private static readonly string DefaultsFolder = Path.Combine(AppContext.BaseDirectory, "Defaults");
    private static readonly string DefaultCustomizationItemsPath = Path.Combine(DefaultsFolder, "DefaultCustomizationItems.json");
    private static readonly string DecryptedCatalogPath = Path.Combine(DefaultsFolder, "Catalog.decrypted.json");

    private static async Task<int> Main()
    {
        try
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
            Extras.Settings.PakPath = Path.Combine(GameInstallDir, "DeadByDaylight", "Content", "Paks");
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

            // Step 4: Generate Bloodweb.json and GetAll.json from the extracted data
            Console.WriteLine("[4/5] Generating Bloodweb.json and GetAll.json...");
            await CatalogGenerator.WriteBloodWebAsync(Path.Combine(FinalOutputFolder, "Bloodweb.json"));
            await CatalogGenerator.WriteGetAllAsync(Path.Combine(FinalOutputFolder, "GetAll.json"));

            Console.WriteLine($"      Bloodweb.json: {CatalogGenerator.BuildCharacterItems().Count} inventory entries");
            Console.WriteLine($"      GetAll.json:   {Classes.Ids.CharacterIds.Count} characters");

            // Step 5: Generate the market files from the default customization items + bundled decrypted catalog
            Console.WriteLine("[5/5] Generating MarketFull.json and MarketDLC.json...");
            var defaultCustomizationIds = CatalogGenerator.LoadDefaultCustomizationItems(DefaultCustomizationItemsPath);
            if (defaultCustomizationIds.Count == 0)
                Console.WriteLine($"      [WARN] No default customization items found at '{DefaultCustomizationItemsPath}'.");

            if (!File.Exists(DecryptedCatalogPath))
                throw new FileNotFoundException($"Decrypted catalog not found at '{DecryptedCatalogPath}'.");

            var catalogIds = CatalogGenerator.LoadCatalogIds(DecryptedCatalogPath);

            await CatalogGenerator.WriteMarketFullAsync(Path.Combine(FinalOutputFolder, "MarketFull.json"), defaultCustomizationIds, catalogIds);
            await CatalogGenerator.WriteMarketDlcAsync(Path.Combine(FinalOutputFolder, "MarketDLC.json"), defaultCustomizationIds);

            Console.WriteLine($"      MarketFull.json: {defaultCustomizationIds.Count} defaults + {catalogIds.Count} catalog entries (characters and bundles excluded)");
            Console.WriteLine($"      MarketDLC.json:  {defaultCustomizationIds.Count} defaults (bundles excluded)");
            Console.WriteLine($"      Saved to: {FinalOutputFolder}");

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] {ex.Message}");
            return 1;
        }
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
