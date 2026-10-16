using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Wolves.Catalog;

/// <summary>
/// Builds <c>Bloodweb.json</c> and <c>GetAll.json</c> from the databases extracted into
/// <see cref="Classes.Ids"/>. The output structure mirrors the game's own responses
/// (see the reference <c>Bloodweb.json</c> / <c>GetAll.json</c>).
/// </summary>
public static class CatalogGenerator
{
    // Quantities granted per entry.
    public const int PerkQuantity = 3;
    public const int ItemQuantity = 50;
    public const int AddonQuantity = 50;
    public const int OfferingQuantity = 50;

    // Shared defaults.
    public const int BloodWebLevel = 30;
    public const int PrestigeLevel = 0;
    public const int LegacyPrestigeLevel = 0;

    // Content shown on the pre-filled blood web rings.
    private const string RingNodeContentId = "Father_Key_Card";
    private const string OuterNodeContentId = "EbonyMementoMori";

    private static readonly JsonSerializerSettings SerializerSettings = new()
    {
        Formatting = Formatting.Indented
    };

    /// <summary>
    /// Builds the single-character response (<c>Bloodweb.json</c>).
    /// </summary>
    /// <param name="characterName">
    /// Backend id of the selected character (e.g. "Dwight"). Empty by default because the
    /// file is a template for whichever character is currently selected.
    /// </param>
    public static BloodWebResponse BuildBloodWeb(string characterName = "")
    {
        return new BloodWebResponse
        {
            BloodWebLevelChanged = false,
            UpdatedWallets = [],
            CharacterName = characterName,
            BloodWebLevel = BloodWebLevel,
            PrestigeLevel = PrestigeLevel,
            BloodWebData = BuildFilledBloodWebData(),
            CharacterItems = BuildCharacterItems(),
            LegacyPrestigeLevel = LegacyPrestigeLevel
        };
    }

    /// <summary>
    /// Builds the all-characters response (<c>GetAll.json</c>), one entry per character in
    /// <see cref="Classes.Ids.CharacterIds"/>, ordered by character index.
    /// </summary>
    public static GetAllResponse BuildGetAll()
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var response = new GetAllResponse();

        foreach (var (_, character) in OrderedCharacters())
        {
            response.List.Add(new CharacterEntry
            {
                CharacterName = character.CharacterId,
                LegacyPrestigeLevel = LegacyPrestigeLevel,
                CharacterItems = BuildCharacterItems(),
                BloodWebLevel = BloodWebLevel,
                BloodWebData = new BloodWebData(),     // empty paths + ringData
                IsEntitled = true,
                IsScopesValid = true,
                Origin = new CharacterOrigin
                {
                    Entitlements = [],
                    PurchaseInfo = new PurchaseInfo
                    {
                        LastUpdateAt = now,
                        ObjectId = character.CharacterId,
                        Quantity = 1
                    }
                },
                PrestigeLevel = PrestigeLevel
            });
        }

        return response;
    }

    /// <summary>
    /// Every item, add-on, perk and offering known to <see cref="Classes.Ids"/>, in that order,
    /// with the configured quantities.
    /// </summary>
    public static List<CharacterItem> BuildCharacterItems()
    {
        var items = new List<CharacterItem>(
            Classes.Ids.ItemIds.Count +
            Classes.Ids.ItemAddonIds.Count +
            Classes.Ids.PerkIds.Count +
            Classes.Ids.OfferingIds.Count);

        items.AddRange(Classes.Ids.ItemIds.Keys.Select(id => new CharacterItem { ItemId = id, Quantity = ItemQuantity }));
        items.AddRange(Classes.Ids.ItemAddonIds.Keys.Select(id => new CharacterItem { ItemId = id, Quantity = AddonQuantity }));
        items.AddRange(Classes.Ids.PerkIds.Keys.Select(id => new CharacterItem { ItemId = id, Quantity = PerkQuantity }));
        items.AddRange(Classes.Ids.OfferingIds.Keys.Select(id => new CharacterItem { ItemId = id, Quantity = OfferingQuantity }));

        return items;
    }

    // ----------------------------------------------------------------------------- market files

    // Written to the "provider" field, e.g. "Wolves (1.1.0)"
    public static readonly string MarketProvider = $"{AppInfo.Name} ({AppInfo.Version})";
    private const string MarketFullFileType = "Market File (Full)";
    private const string MarketDlcFileType = "Market File (DLC)";
    private const int MarketTimestampJitter = 512;   // lastUpdateAt = now - rand(0..512)
    // objectIds containing any of these (case-insensitive) are never added to the market files
    private static readonly string[] MarketExcludedSubstrings =
    [
        "Bundle",
        "NewPlayer",
        "ChapterPack",
        "Autumn_Fashion",
        "Gameplay_Expo",
        "St_Patricks_2026",
        "DeckTheTrials2025"
    ];

    /// <summary>
    /// <c>MarketFull.json</c>: every default customization item plus every entry of the (decrypted)
    /// catalog, excluding entries whose id is a character id.
    /// </summary>
    public static MarketFile BuildMarketFull(IEnumerable<string> defaultCustomizationIds, IEnumerable<string> catalogIds)
    {
        var characterIds = new HashSet<string>(
            Classes.Ids.CharacterIds.Values.Select(c => c.CharacterId),
            StringComparer.Ordinal);

        var market = NewMarketFile(MarketFullFileType);
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        foreach (string id in defaultCustomizationIds.Concat(catalogIds))
        {
            if (characterIds.Contains(id) || IsExcludedFromMarket(id))
                continue;

            market.InventoryItems.Add(NewInventoryItem(now, id));
        }

        return market;
    }

    /// <summary>
    /// <c>MarketDLC.json</c>: the default customization items plus every character
    /// (by <see cref="Classes.Character.CharacterId"/>), which unlocks the DLC characters.
    /// </summary>
    public static MarketFile BuildMarketDlc(IEnumerable<string> defaultCustomizationIds)
    {
        var market = NewMarketFile(MarketDlcFileType);
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var added = new HashSet<string>(StringComparer.Ordinal);

        foreach (string id in defaultCustomizationIds.Concat(CharacterIdsInOrder()))
        {
            if (IsExcludedFromMarket(id) || !added.Add(id))
                continue;

            market.InventoryItems.Add(NewInventoryItem(now, id));
        }

        return market;
    }

    /// <summary>Backend ids of all extracted characters, ordered by character index.</summary>
    public static List<string> CharacterIdsInOrder() =>
        OrderedCharacters()
            .Select(kvp => kvp.Value.CharacterId)
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();

    // ----------------------------------------------------------------------------- items list

    /// <summary>
    /// <c>ItemsList.json</c>: the ids of every perk, item, add-on and offering
    /// (same shape as <c>Catalog_ItemsList()</c> in the PHP API).
    /// </summary>
    public static ItemsListFile BuildItemsList() => new()
    {
        Perks = Classes.Ids.PerkIds.Keys.ToList(),
        Items = Classes.Ids.ItemIds.Keys.ToList(),
        Addons = Classes.Ids.ItemAddonIds.Keys.ToList(),
        Offerings = Classes.Ids.OfferingIds.Keys.ToList()
    };

    public static async Task WriteItemsListAsync(string path) =>
        await File.WriteAllTextAsync(path, ToJson(BuildItemsList()));

    /// <summary>
    /// <c>CharactersList.json</c>: a plain array of character ids
    /// (same shape as <c>Catalog_CharactersList()</c> in the PHP API).
    /// </summary>
    public static async Task WriteCharactersListAsync(string path) =>
        await File.WriteAllTextAsync(path, ToJson(CharacterIdsInOrder()));

    /// <summary>
    /// Reads a JSON string array of customization ids (e.g. <c>Defaults/DefaultCustomizationItems.json</c>).
    /// Returns an empty list if the file does not exist.
    /// </summary>
    public static List<string> LoadDefaultCustomizationItems(string path)
    {
        if (!File.Exists(path))
            return [];

        return JsonConvert.DeserializeObject<List<string>>(File.ReadAllText(path)) ?? [];
    }

    /// <summary>
    /// Reads every top-level <c>id</c> from a decrypted catalog (a JSON array of objects).
    /// </summary>
    public static List<string> LoadCatalogIds(string decryptedCatalogPath)
    {
        var catalog = JArray.Parse(File.ReadAllText(decryptedCatalogPath));

        return catalog
            .OfType<JObject>()
            .Select(entry => entry["id"]?.ToString())
            .Where(id => !string.IsNullOrEmpty(id))
            .Select(id => id!)
            .ToList();
    }

    public static async Task WriteMarketFullAsync(string path, IEnumerable<string> defaultCustomizationIds, IEnumerable<string> catalogIds) =>
        await File.WriteAllTextAsync(path, ToJson(BuildMarketFull(defaultCustomizationIds, catalogIds)));

    public static async Task WriteMarketDlcAsync(string path, IEnumerable<string> defaultCustomizationIds) =>
        await File.WriteAllTextAsync(path, ToJson(BuildMarketDlc(defaultCustomizationIds)));

    /// <summary>Ids matching any <see cref="MarketExcludedSubstrings"/> entry are never written to the market files.</summary>
    private static bool IsExcludedFromMarket(string objectId) =>
        MarketExcludedSubstrings.Any(word => objectId.Contains(word, StringComparison.OrdinalIgnoreCase));

    private static MarketFile NewMarketFile(string fileType) => new()
    {
        FileType = fileType,
        CreationDate = DateTime.Now.ToString("dd.MM.yyyy HH:mm"),   // PHP: date("d.m.Y H:i")
        Provider = MarketProvider
    };

    private static InventoryItem NewInventoryItem(long now, string objectId) => new()
    {
        LastUpdateAt = now - Random.Shared.Next(0, MarketTimestampJitter + 1),
        ObjectId = objectId,
        Quantity = 1
    };

    // ----------------------------------------------------------------------------- serialisation

    public static string ToJson(object value) => JsonConvert.SerializeObject(value, SerializerSettings);

    public static async Task WriteBloodWebAsync(string path, string characterName = "") =>
        await File.WriteAllTextAsync(path, ToJson(BuildBloodWeb(characterName)));

    public static async Task WriteGetAllAsync(string path) =>
        await File.WriteAllTextAsync(path, ToJson(BuildGetAll()));

    // ----------------------------------------------------------------------------- helpers

    private static IEnumerable<KeyValuePair<string, Classes.Character>> OrderedCharacters() =>
        Classes.Ids.CharacterIds.OrderBy(kvp => int.TryParse(kvp.Key, out int index) ? index : int.MaxValue)
                                .ThenBy(kvp => kvp.Key, StringComparer.Ordinal);

    /// <summary>
    /// The pre-filled blood web: centre node collected, three full rings of collected
    /// <c>Father_Key_Card</c> nodes and an available Ebony Memento Mori on the outer ring.
    /// </summary>
    private static BloodWebData BuildFilledBloodWebData()
    {
        var data = new BloodWebData();

        // Centre -> ring 1
        for (int node = 101; node <= 106; node++)
            data.Paths.Add($"0_{node}");

        // Ring 1 loop
        data.Paths.AddRange(RingLoop(101, 106));

        // Ring 1 -> ring 2 (each ring-1 node feeds two ring-2 nodes)
        for (int i = 0; i < 6; i++)
        {
            int from = 101 + i;
            data.Paths.Add($"{from}_{201 + i * 2}");
            data.Paths.Add($"{from}_{202 + i * 2}");
        }

        // Ring 2 loop
        data.Paths.AddRange(RingLoop(201, 212));

        // Ring 3 loop
        data.Paths.AddRange(RingLoop(301, 312));

        data.RingData.Add(new BloodWebRing
        {
            NodeData = [new BloodWebNode { NodeId = 0, State = BloodWebNodeState.Collected }]
        });
        data.RingData.Add(CollectedRing(101, 106));
        data.RingData.Add(CollectedRing(201, 212));
        data.RingData.Add(CollectedRing(301, 318));
        data.RingData.Add(new BloodWebRing
        {
            NodeData =
            [
                new BloodWebNode { ContentId = OuterNodeContentId, NodeId = 401, State = BloodWebNodeState.Available }
            ]
        });

        return data;
    }

    /// <summary>Paths that connect consecutive nodes in a ring and close the loop.</summary>
    private static IEnumerable<string> RingLoop(int first, int last)
    {
        for (int node = first; node < last; node++)
            yield return $"{node}_{node + 1}";
        yield return $"{last}_{first}";
    }

    private static BloodWebRing CollectedRing(int first, int last)
    {
        var ring = new BloodWebRing();
        for (int node = first; node <= last; node++)
        {
            ring.NodeData.Add(new BloodWebNode
            {
                ContentId = RingNodeContentId,
                NodeId = node,
                State = BloodWebNodeState.Collected
            });
        }
        return ring;
    }
}
