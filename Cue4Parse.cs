using CUE4Parse.Compression;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.GameTypes.DBD.Encryption.Aes;
using CUE4Parse.MappingsProvider;
using CUE4Parse.MappingsProvider.Usmap;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Wolves;

public static class Cue4Parse
{
    private static DefaultFileProvider? Provider { get; set; }

    private const string OodleDllName = "oodle-data-shared.dll";

    public static void Initialize()
    {
        // Oodle builds downloaded by CUE4Parse are x64 only; a 32-bit process cannot load them
        if (!Environment.Is64BitProcess)
        {
            throw new PlatformNotSupportedException(
                "Wolves is running as a 32-bit process, but the Oodle decompressor is 64-bit only. " +
                "Build/publish for win-x64 (not win-x86).");
        }

        if (!Directory.Exists(Extras.Settings.PakPath))
            throw new DirectoryNotFoundException($"Paks folder not found: '{Extras.Settings.PakPath}'.");

        if (!File.Exists(Extras.Settings.MappingsPath))
            throw new FileNotFoundException($"Mappings file not found: '{Extras.Settings.MappingsPath}'.", Extras.Settings.MappingsPath);

        // CUE4Parse downloads the DLL into the working directory, which Program pins to the exe folder
        string oodlePath = Path.Combine(AppContext.BaseDirectory, OodleDllName);
        if (!File.Exists(oodlePath))
        {
            Console.WriteLine($"      Downloading {OodleDllName}...");
            OodleHelper.DownloadOodleDll();
        }

        if (!File.Exists(oodlePath))
        {
            throw new FileNotFoundException(
                $"'{OodleDllName}' is missing and could not be downloaded (no internet access, or GitHub is blocked). " +
                $"Download it manually and place it next to Wolves.exe: '{oodlePath}'.", oodlePath);
        }

        try
        {
            OodleHelper.Initialize(oodlePath);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to load '{oodlePath}'. The file may be corrupted or built for another architecture; delete it to re-download.", ex);
        }

        var versionContainer = new VersionContainer(EGame.GAME_DeadByDaylight);
        Provider = new DefaultFileProvider(
            Extras.Settings.PakPath,
            SearchOption.AllDirectories,
            versionContainer,
            StringComparer.OrdinalIgnoreCase
        )
        {
            CustomEncryption = DBDAes.DbDDecrypt
        };

        Provider.Initialize();
        if (Provider.UnloadedVfs.Count + Provider.MountedVfs.Count == 0)
            throw new InvalidOperationException($"No .pak/.utoc archives found in '{Extras.Settings.PakPath}'.");

        Provider.SubmitKey(new FGuid(0), new FAesKey(Extras.Settings.AesKey));
        Provider.PostMount();

        var aesMax = Provider.RequiredKeys.Count + Provider.Keys.Count;
        var archiveMax = Provider.UnloadedVfs.Count + Provider.MountedVfs.Count;
        Console.WriteLine($"      Mounted {Provider.MountedVfs.Count}/{archiveMax} archives ({Provider.Keys.Count}/{aesMax} keys).");

        if (Provider.MountedVfs.Count == 0)
        {
            throw new InvalidOperationException(
                $"Found {archiveMax} archives but none could be mounted. Check the warnings above: " +
                "'Could not load file or assembly' means a DLL that CUE4Parse.dll needs is missing next to the exe; " +
                "otherwise the AES key in Models.cs does not match this game build, or the build is not supported by CUE4Parse.dll.");
        }

        Provider.LoadVirtualPaths(new FPackageFileVersion(522, (int)EUnrealEngineObjectUE5Version.ADD_SOFTOBJECTPATH_LIST));
        Provider.TryChangeCulture(Provider.GetLanguageCode(ELanguage.English));

        try
        {
            Provider.MappingsContainer = new FileUsmapTypeMappingsProvider(Extras.Settings.MappingsPath);
        }
        catch (Exception ex)
        {
            throw new InvalidDataException($"Failed to read mappings '{Extras.Settings.MappingsPath}'. Make sure it is a valid .usmap for this game build.", ex);
        }
    }

    public static void Get_Files()
    {
        if (Provider is null)
        {
            throw new InvalidOperationException("CUE4Parse Provider is not initialized.");
        }

        Classes.FilePaths.Clear();

        foreach (var kvp in Provider.Files)
        {
            switch (kvp.Value.Name)
            {
                case "ItemDB.uasset":
                    Classes.FilePaths.ItemDb.Add(kvp.Value.Path);
                    break;
                case "OfferingDB.uasset":
                    Classes.FilePaths.OfferingDb.Add(kvp.Value.Path);
                    break;
                case "ItemAddonDB.uasset":
                    Classes.FilePaths.ItemAddonDb.Add(kvp.Value.Path);
                    break;
                case "PerkDB.uasset":
                    Classes.FilePaths.PerkDb.Add(kvp.Value.Path);
                    break;
                case "CharacterDescriptionDB.uasset":
                    Classes.FilePaths.CharacterDb.Add(kvp.Value.Path);
                    break;
            }
        }

        Add_Values(Classes.FilePaths.ItemDb, "ItemDB");
        Add_Values(Classes.FilePaths.ItemAddonDb, "ItemAddonDB");
        Add_Values(Classes.FilePaths.OfferingDb, "OfferingDB");
        Add_Values(Classes.FilePaths.PerkDb, "PerkDB");
        Add_Values(Classes.FilePaths.CharacterDb, "CharacterDescriptionDB");
    }

    private static string ToRole(JToken? role) =>
        role?.ToString() == "EPlayerRole::VE_Camper" ? "Camper" : "Slasher";

    private static string LocalizedName(JObject? property) =>
        property?["UIData"]?["DisplayName"]?["LocalizedString"]?.ToString() ?? "None";

    private static void Add_Values(List<string> list, string type)
    {
        if (Provider is null) return;

        foreach (var item in list)
        {
            var export = JsonConvert.DeserializeObject<dynamic>(
                JsonConvert.SerializeObject(Provider.LoadPackage(item).GetExports()));

            foreach (JProperty p in export?[0]?.Rows ?? Enumerable.Empty<JProperty>())
            {
                // p.Name is the DataTable row key; the row's struct is the single child object.
                string rowName = p.Name;

                foreach (var property in p.Values<JObject>())
                {
                    var itemId = property?["ItemId"]?.ToString() ?? "None";

                    switch (type)
                    {
                        case "ItemDB":
                            Classes.Ids.ItemIds[itemId] = new Classes.Base
                            {
                                Role = ToRole(property?["Role"]),
                                Name = LocalizedName(property)
                            };
                            break;

                        case "ItemAddonDB":
                            Classes.Ids.ItemAddonIds[itemId] = new Classes.Base
                            {
                                Role = ToRole(property?["Role"]),
                                Name = LocalizedName(property)
                            };
                            break;

                        case "OfferingDB":
                            Classes.Ids.OfferingIds[itemId] = new Classes.Base
                            {
                                Role = ToRole(property?["Role"]),
                                Name = LocalizedName(property)
                            };
                            break;

                        case "PerkDB":
                            Classes.Ids.PerkIds[itemId] = new Classes.Perk
                            {
                                Role = ToRole(property?["Role"]),
                                Name = LocalizedName(property),
                                Character = property?["AssociatedPlayerIndex"] != null
                                    ? Convert.ToInt32(property?["AssociatedPlayerIndex"])
                                    : 0
                            };
                            break;

                        case "CharacterDescriptionDB":
                            string characterIndex = property?["CharacterIndex"]?.ToString() ?? "None";
                            if (characterIndex == "-1") continue;

                            Classes.Ids.CharacterIds[characterIndex] = new Classes.Character
                            {
                                // Backend identifier ("Dwight", "Chuckles", "Bear", ...). The row has an
                                // CharacterId field; the DataTable row key carries the same value, so fall back to it.
                                CharacterId = property?["CharacterId"]?.ToString() is { Length: > 0 } characterId ? characterId : rowName,
                                Role = ToRole(property?["Role"]),
                                Name = property?["DisplayName"]?["LocalizedString"]?.ToString() ?? "None",
                                Gender = property?["Gender"]?.ToString() == "EDBDGender::VE_Female" ? "Female" : "Male",
                                ParentItem = property?["DefaultItem"]?.ToString() ?? "None",
                                DLC = property?["ChapterDlcId"]?.ToString() ?? "None",
                                Difficulty = property?["Difficulty"]?.ToString().Replace("ECharacterDifficulty::VE_", "") ?? "None",
                                BackStory = property?["BackStory"]?["LocalizedString"]?.ToString() ?? "None",
                                Biography = property?["Biography"]?["LocalizedString"]?.ToString() ?? "None",
                                IconFilePath = property?["IconFilePath"]?.ToString() ?? "None",
                                BackgroundImagePath = property?["BackgroundImagePath"]?.ToString() ?? "None",
                            };
                            break;
                    }
                }
            }
        }
    }

    public static bool IsListEmpty()
    {
        return Classes.Ids.ItemIds.Count == 0;
    }
}
