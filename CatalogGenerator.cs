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

    // ----------------------------------------------------------------------------- unlocked catalog

    // metaData dates written by Catalog_Unlock() in the PHP API
    public const string UnlockStartDate = "2018-01-01T00:00:00";   // newStartDate, releaseDate
    public const string UnlockEndDate = "2033-01-01T00:00:00";     // newEndDate

    private const int IsoDateLength = 19;   // yyyy-MM-ddTHH:mm:ss
    private const string UnbreakableKey = "unbreakable";
    private static readonly byte[] LiteralTrue = "true"u8.ToArray();
    private static readonly byte[] LiteralNull = "null"u8.ToArray();
    private static readonly byte[] LiteralFalse = "false"u8.ToArray();

    /// <summary>What happens to <c>"unbreakable": true</c> in <c>metaData</c>.</summary>
    public enum UnbreakableMode
    {
        /// <summary>Leave as is.</summary>
        Keep,

        /// <summary><c>true</c> -> <c>null</c> (same 4 bytes).</summary>
        Null,

        /// <summary><c>"unbreakable": true</c> -> <c>"unbreakable":false</c>; the extra byte is taken from the space after the colon, so the size is preserved.</summary>
        FalseCompact,

        /// <summary><c>"unbreakable": true</c> -> <c>"unbreakable": false</c>; formatting kept, but the file grows by 1 byte per value.</summary>
        FalseSpaced
    }

    /// <param name="ReplaceDates">Move <c>newStartDate</c> / <c>releaseDate</c> to <paramref name="StartDate"/> and <c>newEndDate</c> to <paramref name="EndDate"/>.</param>
    /// <param name="Unbreakable">How to rewrite <c>"unbreakable": true</c>.</param>
    /// <param name="StartDate">Must be exactly <c>yyyy-MM-ddTHH:mm:ss</c> so the size does not change.</param>
    /// <param name="EndDate">Must be exactly <c>yyyy-MM-ddTHH:mm:ss</c> so the size does not change.</param>
    public sealed record UnlockOptions(
        bool ReplaceDates = true,
        UnbreakableMode Unbreakable = UnbreakableMode.Null,
        string StartDate = UnlockStartDate,
        string EndDate = UnlockEndDate);

    /// <param name="SizeDelta">Output size minus input size; 0 unless <see cref="UnbreakableMode.FalseSpaced"/> is used.</param>
    public sealed record UnlockResult(int DatesReplaced, int DatesSkipped, int UnbreakableReplaced, int UnbreakableSkipped, long Size, long SizeDelta);

    /// <summary>
    /// <c>CatalogUnlocked.json</c>: the decrypted catalog with the <c>metaData</c> dates moved so every item
    /// counts as released and not expired (same as <c>Catalog_Unlock()</c> in the PHP API), and
    /// <c>"unbreakable": true</c> rewritten; each step can be switched off through <paramref name="options"/>.
    /// The file is patched byte by byte instead of being re-serialised, so the output keeps the same
    /// formatting and, unless <see cref="UnbreakableMode.FalseSpaced"/> is chosen, the exact same size.
    /// </summary>
    public static UnlockResult WriteUnlockedCatalog(string path, string decryptedCatalogPath, UnlockOptions? options = null)
    {
        options ??= new UnlockOptions();
        byte[] startDate = ToIsoDateBytes(options.StartDate, nameof(options.StartDate));
        byte[] endDate = ToIsoDateBytes(options.EndDate, nameof(options.EndDate));

        byte[] data = File.ReadAllBytes(decryptedCatalogPath);
        long originalSize = data.Length;

        var growPositions = new List<int>();
        var result = UnlockInPlace(data, options, startDate, endDate, growPositions);
        byte[] output = growPositions.Count > 0 ? InsertFalseLiterals(data, growPositions) : data;

        // Only FalseSpaced may change the size, and only by exactly 1 byte per value
        long expectedSize = originalSize + growPositions.Count;
        if (output.Length != expectedSize)
            throw new InvalidOperationException($"Unlocked catalog size is {output.Length}, expected {expectedSize}; refusing to write it.");

        File.WriteAllBytes(path, output);
        return result with { Size = output.Length, SizeDelta = output.Length - originalSize };
    }

    /// <summary>Copies <paramref name="data"/>, writing "false" in place of the 4-byte "true" at each position.</summary>
    private static byte[] InsertFalseLiterals(byte[] data, List<int> truePositions)
    {
        var output = new byte[data.Length + truePositions.Count];
        int source = 0, target = 0;

        foreach (int position in truePositions)
        {
            int length = position - source;
            Buffer.BlockCopy(data, source, output, target, length);
            target += length;

            LiteralFalse.CopyTo(output, target);
            target += LiteralFalse.Length;
            source = position + LiteralTrue.Length;
        }

        Buffer.BlockCopy(data, source, output, target, data.Length - source);
        return output;
    }

    private static byte[] ToIsoDateBytes(string date, string name)
    {
        byte[] bytes = System.Text.Encoding.ASCII.GetBytes(date);
        if (bytes.Length != IsoDateLength || !IsIsoDate(bytes, 0, bytes.Length))
            throw new ArgumentException($"{name} must be exactly yyyy-MM-ddTHH:mm:ss (got '{date}').", name);
        return bytes;
    }

    private sealed class JsonFrame
    {
        public bool IsObject;
        public bool ExpectKey;
        public string? Key;
    }

    /// <summary>
    /// Minimal JSON scanner over raw UTF-8 bytes (structural characters are ASCII, so multi-byte
    /// text can never be mistaken for them). Rewrites date strings at <c>[i].metaData.&lt;key&gt;</c>
    /// and <c>[i].metaData.unbreakable: true</c>. Every change keeps the same byte length.
    /// Values that are not "yyyy-MM-ddTHH:mm:ss..." strings (null, empty, other formats) are left alone.
    /// </summary>
    /// <param name="growPositions">Receives the positions of "true" literals that FalseSpaced must widen (not patched in place).</param>
    private static UnlockResult UnlockInPlace(byte[] json, UnlockOptions options, byte[] startDate, byte[] endDate, List<int> growPositions)
    {
        var dates = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["newStartDate"] = startDate,
            ["releaseDate"] = startDate,
            ["newEndDate"] = endDate
        };

        var frames = new List<JsonFrame>();
        int datesReplaced = 0, datesSkipped = 0, unbreakableReplaced = 0, unbreakableSkipped = 0;
        int i = 0;

        while (i < json.Length)
        {
            switch (json[i])
            {
                case (byte)'"':
                {
                    int end = FindStringEnd(json, i);
                    JsonFrame? top = frames.Count > 0 ? frames[^1] : null;

                    if (top is { IsObject: true, ExpectKey: true })
                    {
                        top.Key = System.Text.Encoding.UTF8.GetString(json, i + 1, end - i - 1);
                        top.ExpectKey = false;
                    }
                    else if (options.ReplaceDates && IsCatalogMetaData(frames) && top!.Key is { } key &&
                             dates.TryGetValue(key, out byte[]? date))
                    {
                        if (IsIsoDate(json, i + 1, end))
                        {
                            Buffer.BlockCopy(date, 0, json, i + 1, IsoDateLength);   // same length: size is preserved
                            datesReplaced++;
                        }
                        else
                        {
                            datesSkipped++;
                        }
                    }

                    i = end + 1;
                    continue;
                }

                // Literals are never inside strings here: strings are skipped as a whole above
                case (byte)'t':
                    if (options.Unbreakable != UnbreakableMode.Keep && IsCatalogMetaData(frames) &&
                        frames[^1] is { ExpectKey: false, Key: UnbreakableKey } &&
                        json.AsSpan(i).StartsWith(LiteralTrue))
                    {
                        if (options.Unbreakable == UnbreakableMode.Null)
                        {
                            LiteralNull.CopyTo(json, i);
                            unbreakableReplaced++;
                        }
                        else if (options.Unbreakable == UnbreakableMode.FalseSpaced)
                        {
                            growPositions.Add(i);   // widened when the output is built
                            unbreakableReplaced++;
                        }
                        else if (i > 0 && (json[i - 1] == (byte)' ' || json[i - 1] == (byte)'\t'))
                        {
                            // "key": true -> "key":false (5 bytes into the 4 + 1 whitespace byte)
                            LiteralFalse.CopyTo(json, i - 1);
                            unbreakableReplaced++;
                        }
                        else
                        {
                            unbreakableSkipped++;   // no spare byte: changing it would change the size
                        }

                        i += LiteralTrue.Length;
                        continue;
                    }
                    break;

                case (byte)'{':
                    frames.Add(new JsonFrame { IsObject = true, ExpectKey = true });
                    break;

                case (byte)'[':
                    frames.Add(new JsonFrame { IsObject = false });
                    break;

                case (byte)'}':
                case (byte)']':
                    if (frames.Count == 0)
                        throw new InvalidDataException("Malformed catalog JSON: unbalanced brackets.");
                    frames.RemoveAt(frames.Count - 1);
                    break;

                case (byte)',':
                    if (frames.Count > 0 && frames[^1].IsObject)
                        frames[^1].ExpectKey = true;
                    break;
            }

            i++;
        }

        return new UnlockResult(datesReplaced, datesSkipped, unbreakableReplaced, unbreakableSkipped, json.Length, 0);
    }

    // Root array -> catalog entry object -> "metaData" object
    private static bool IsCatalogMetaData(List<JsonFrame> frames) =>
        frames.Count == 3 &&
        !frames[0].IsObject &&
        frames[1].IsObject && frames[1].Key == "metaData" &&
        frames[2].IsObject;

    /// <summary>Index of the closing quote of the string that opens at <paramref name="start"/>.</summary>
    private static int FindStringEnd(byte[] json, int start)
    {
        int i = start + 1;
        while (i < json.Length)
        {
            if (json[i] == (byte)'\\')
                i += 2;
            else if (json[i] == (byte)'"')
                return i;
            else
                i++;
        }

        throw new InvalidDataException("Malformed catalog JSON: unterminated string.");
    }

    private static bool IsIsoDate(byte[] json, int start, int end)
    {
        if (end - start < IsoDateLength)
            return false;

        for (int k = 0; k < IsoDateLength; k++)
        {
            byte c = json[start + k];
            bool ok = k switch
            {
                4 or 7 => c == (byte)'-',
                10 => c == (byte)'T',
                13 or 16 => c == (byte)':',
                _ => c >= (byte)'0' && c <= (byte)'9'
            };

            if (!ok)
                return false;
        }

        return true;
    }

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
