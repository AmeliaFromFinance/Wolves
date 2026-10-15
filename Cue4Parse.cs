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

    public static void Initialize()
    {
        OodleHelper.DownloadOodleDll();
        if (!File.Exists("oodle-data-shared.dll"))
        {
            throw new FileNotFoundException("Oodle DLL not found. Please ensure 'oodle-data-shared.dll' is present in the working directory.");
        }

        OodleHelper.Initialize("oodle-data-shared.dll");
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
        Provider.SubmitKey(new FGuid(0), new FAesKey(Extras.Settings.AesKey));
        Provider.PostMount();

        var aesMax = Provider.RequiredKeys.Count + Provider.Keys.Count;
        var archiveMax = Provider.UnloadedVfs.Count + Provider.MountedVfs.Count;
        Console.WriteLine($"      Mounted {Provider.MountedVfs.Count}/{archiveMax} archives ({Provider.Keys.Count}/{aesMax} keys).");

        Provider.LoadVirtualPaths(new FPackageFileVersion(522, (int)EUnrealEngineObjectUE5Version.ADD_SOFTOBJECTPATH_LIST));
        Provider.TryChangeCulture(Provider.GetLanguageCode(ELanguage.English));
        Provider.MappingsContainer = new FileUsmapTypeMappingsProvider(Extras.Settings.MappingsPath);
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
