using Newtonsoft.Json;

namespace Wolves.Catalog;

/// <summary>
/// One inventory entry on a character: <c>{ "itemId": "...", "quantity": n }</c>.
/// </summary>
public sealed class CharacterItem
{
    [JsonProperty("itemId")]
    public string ItemId { get; set; } = string.Empty;

    [JsonProperty("quantity")]
    public int Quantity { get; set; }
}

/// <summary>
/// One node on a blood web ring. The centre node (id 0) has no <c>contentId</c>.
/// </summary>
public sealed class BloodWebNode
{
    [JsonProperty("contentId", NullValueHandling = NullValueHandling.Ignore)]
    public string? ContentId { get; set; }

    [JsonProperty("nodeId")]
    public int NodeId { get; set; }

    [JsonProperty("state")]
    public string State { get; set; } = BloodWebNodeState.Available;
}

public static class BloodWebNodeState
{
    public const string Collected = "Collected";
    public const string Available = "Available";
}

public sealed class BloodWebRing
{
    [JsonProperty("nodeData")]
    public List<BloodWebNode> NodeData { get; set; } = [];
}

public sealed class BloodWebData
{
    [JsonProperty("paths")]
    public List<string> Paths { get; set; } = [];

    [JsonProperty("ringData")]
    public List<BloodWebRing> RingData { get; set; } = [];
}

/// <summary>
/// Shape of <c>Bloodweb.json</c>: the blood web / inventory of the currently selected character.
/// </summary>
public sealed class BloodWebResponse
{
    [JsonProperty("bloodWebLevelChanged")]
    public bool BloodWebLevelChanged { get; set; }

    [JsonProperty("updatedWallets")]
    public List<object> UpdatedWallets { get; set; } = [];

    [JsonProperty("characterName")]
    public string CharacterName { get; set; } = string.Empty;

    [JsonProperty("bloodWebLevel")]
    public int BloodWebLevel { get; set; }

    [JsonProperty("prestigeLevel")]
    public int PrestigeLevel { get; set; }

    [JsonProperty("bloodWebData")]
    public BloodWebData BloodWebData { get; set; } = new();

    [JsonProperty("characterItems")]
    public List<CharacterItem> CharacterItems { get; set; } = [];

    [JsonProperty("legacyPrestigeLevel")]
    public int LegacyPrestigeLevel { get; set; }
}

public sealed class PurchaseInfo
{
    [JsonProperty("lastUpdateAt")]
    public long LastUpdateAt { get; set; }

    [JsonProperty("objectId")]
    public string ObjectId { get; set; } = string.Empty;

    [JsonProperty("quantity")]
    public int Quantity { get; set; } = 1;
}

public sealed class CharacterOrigin
{
    [JsonProperty("entitlements")]
    public List<object> Entitlements { get; set; } = [];

    [JsonProperty("purchaseInfo")]
    public PurchaseInfo PurchaseInfo { get; set; } = new();
}

/// <summary>
/// One entry in the <c>GetAll.json</c> list: a single character's full state.
/// </summary>
public sealed class CharacterEntry
{
    [JsonProperty("characterName")]
    public string CharacterName { get; set; } = string.Empty;

    [JsonProperty("legacyPrestigeLevel")]
    public int LegacyPrestigeLevel { get; set; }

    [JsonProperty("characterItems")]
    public List<CharacterItem> CharacterItems { get; set; } = [];

    [JsonProperty("bloodWebLevel")]
    public int BloodWebLevel { get; set; }

    [JsonProperty("bloodWebData")]
    public BloodWebData BloodWebData { get; set; } = new();

    [JsonProperty("isEntitled")]
    public bool IsEntitled { get; set; } = true;

    [JsonProperty("isScopesValid")]
    public bool IsScopesValid { get; set; } = true;

    [JsonProperty("origin")]
    public CharacterOrigin Origin { get; set; } = new();

    [JsonProperty("prestigeLevel")]
    public int PrestigeLevel { get; set; }
}

/// <summary>
/// Shape of <c>GetAll.json</c>: every character's state.
/// </summary>
public sealed class GetAllResponse
{
    [JsonProperty("list")]
    public List<CharacterEntry> List { get; set; } = [];
}

/// <summary>
/// One entry in a market file's <c>inventoryItems</c>.
/// </summary>
public sealed class InventoryItem
{
    [JsonProperty("lastUpdateAt")]
    public long LastUpdateAt { get; set; }

    [JsonProperty("objectId")]
    public string ObjectId { get; set; } = string.Empty;

    [JsonProperty("quantity")]
    public int Quantity { get; set; } = 1;
}

/// <summary>
/// Shape of <c>MarketFull.json</c> / <c>MarketDLC.json</c>.
/// </summary>
public sealed class MarketFile
{
    [JsonProperty("fileType")]
    public string FileType { get; set; } = string.Empty;

    [JsonProperty("creationDate")]
    public string CreationDate { get; set; } = string.Empty;

    [JsonProperty("provider")]
    public string Provider { get; set; } = string.Empty;

    [JsonProperty("inventoryItems")]
    public List<InventoryItem> InventoryItems { get; set; } = [];
}

/// <summary>
/// Shape of <c>ItemsList.json</c>: plain id lists per category.
/// </summary>
public sealed class ItemsListFile
{
    [JsonProperty("perks")]
    public List<string> Perks { get; set; } = [];

    [JsonProperty("items")]
    public List<string> Items { get; set; } = [];

    [JsonProperty("addons")]
    public List<string> Addons { get; set; } = [];

    [JsonProperty("offerings")]
    public List<string> Offerings { get; set; } = [];
}
