# Wolves Asset Extractor

Mounts a local Dead by Daylight installation with CUE4Parse, extracts the item / add-on /
offering / perk / character databases, and generates `Bloodweb.json`, `GetAll.json`,
`MarketFull.json` and `MarketDLC.json` in the same shape as the game's own responses.

## Project layout

| File | Purpose |
| :--- | :--- |
| `Program.cs` | Pipeline orchestration (the five steps below). |
| `Cue4Parse.cs` | Mounting the paks and parsing the `*DB.uasset` DataTables into `Classes.Ids`. |
| `Models.cs` | `Extras.Settings` (pak path, AES key, mappings path) and the `Classes.*` data model. |
| `FileOpenDialog.cs` | Native Windows `IFileOpenDialog` wrapper used to pick the `.usmap` file (no WinForms/WPF dependency). |
| `CatalogModels.cs` | Serialisation models for the generated JSON files. |
| `CatalogGenerator.cs` | Builds `Bloodweb.json`, `GetAll.json`, `MarketFull.json` and `MarketDLC.json`. |
| `Defaults/DefaultCustomizationItems.json` | JSON string array of default customization ids used by the market files. |
| `Defaults/Catalog.decrypted.json` | Decrypted store catalog; its top-level `id`s are used by `MarketFull.json`. |

## Configuration

### `Program.cs`
| Variable | Default Value | Description |
| :--- | :--- | :--- |
| `GameInstallDir` | `C:\Program Files (x86)\Steam\steamapps\common\Dead by Daylight` | Root installation directory of the game. `Paks` are resolved as `<GameInstallDir>\DeadByDaylight\Content\Paks`. |
| `FinalOutputFolder` | `<exe folder>\Extracted` | Destination for `mappings.usmap`, the five extracted databases and all generated JSON files. |
| `DefaultCustomizationItemsPath` | `<exe folder>\Defaults\DefaultCustomizationItems.json` | Default customization ids. Optional: a warning is printed and the lists are empty if missing. |
| `DecryptedCatalogPath` | `<exe folder>\Defaults\Catalog.decrypted.json` | Decrypted catalog (JSON array of objects with an `id`). Required for step 5. |

### `Models.cs`
| Variable | Default Value | Description |
| :--- | :--- | :--- |
| `Extras.Settings.PakPath` | `<GameInstallDir>\DeadByDaylight\Content\Paks` | Explicit path to the game's `.pak` files (overwritten from `GameInstallDir` at runtime). |
| `Extras.Settings.AesKey` | `0x22B1639B…39B3` | AES key used by CUE4Parse to decrypt the game's packages. |

### `CatalogGenerator.cs`
| Constant | Default Value | Description |
| :--- | :--- | :--- |
| `PerkQuantity` | `3` | Quantity written for every perk. |
| `ItemQuantity` / `AddonQuantity` / `OfferingQuantity` | `50` | Quantity written for every item, add-on and offering. |
| `BloodWebLevel` | `30` | `bloodWebLevel` written to `Bloodweb.json` and `GetAll.json`. |
| `PrestigeLevel` / `LegacyPrestigeLevel` | `0` | Prestige values written to both files. |
| `MarketProvider` | `Cursed Market API` | `provider` written to the market files. |

## Execution flow

1. **Select mappings:** A standard Windows *Open* dialog asks for a `*.usmap` file. The selected file is copied to `<FinalOutputFolder>\mappings.usmap` (the original is left untouched). Cancelling the dialog exits the program.
2. **Mount game:** CUE4Parse mounts the game packages using the mappings from step 1.
3. **Asset extraction:** `ItemDB`, `ItemAddonDB`, `OfferingDB`, `PerkDB` and `CharacterDescriptionDB` are parsed and written to `Items.json`, `Addons.json`, `Offerings.json`, `Perks.json` and `Characters.json`.
4. **Generate catalog files:** `Bloodweb.json` and `GetAll.json` are generated from the extracted data.
5. **Generate market files:** `MarketFull.json` and `MarketDLC.json` are generated from `Defaults\DefaultCustomizationItems.json` and `Defaults\Catalog.decrypted.json`.

> [!NOTE]
> `Defaults\Catalog.decrypted.json` is a snapshot of the store catalog. Replace it with a newer
> decrypted catalog when the game updates; the program only reads each entry's top-level `id`.

## Generated files

### `Bloodweb.json`
Single-character response (`characterName` is left empty, as it describes whichever character is
currently selected). `bloodWebData` is pre-filled: node `0` collected, rings 1–3 fully collected
(`Father_Key_Card`) and an available `EbonyMementoMori` on node `401`. `characterItems` contains
**every** item, add-on, perk and offering from `Classes.Ids`, in that order, with the quantities above.

```json
{
    "bloodWebLevelChanged": false,
    "updatedWallets": [],
    "characterName": "",
    "bloodWebLevel": 30,
    "prestigeLevel": 0,
    "bloodWebData": { "paths": [ "0_101", … ], "ringData": [ { "nodeData": [ … ] }, … ] },
    "characterItems": [ { "itemId": "Item_Camper_DullKey", "quantity": 50 }, …, { "itemId": "Bond", "quantity": 3 }, … ],
    "legacyPrestigeLevel": 0
}
```

### `GetAll.json`
One entry per character in `Classes.Ids.CharacterIds`, ordered by character index. `characterName`
and `origin.purchaseInfo.objectId` use the character's backend id (`Classes.Character.CharacterId`, read
from the `CharacterId` field of `CharacterDescriptionDB`, e.g. `Dwight`, `Chuckles`, `Bear`). Every
character receives the same full `characterItems` list.

```json
{
    "list": [
        {
            "characterName": "Dwight",
            "legacyPrestigeLevel": 0,
            "characterItems": [ … ],
            "bloodWebLevel": 30,
            "bloodWebData": { "paths": [], "ringData": [] },
            "isEntitled": true,
            "isScopesValid": true,
            "origin": {
                "entitlements": [],
                "purchaseInfo": { "lastUpdateAt": 1760536260, "objectId": "Dwight", "quantity": 1 }
            },
            "prestigeLevel": 0
        }
    ]
}
```

### `MarketFull.json` / `MarketDLC.json`
**Full** = every default customization item followed by every top-level `id` in the decrypted catalog,
with entries whose id is a character id (`Classes.Character.CharacterId`) removed. **DLC** = only the
default customization items. In both files any id containing `Bundle` (case-insensitive) is skipped.
`lastUpdateAt` is `now - rand(0..512)` seconds and `creationDate` uses `dd.MM.yyyy HH:mm`.

```json
{
    "fileType": "Market File (Full)",
    "creationDate": "15.10.2026 16:12",
    "provider": "Cursed Market API",
    "inventoryItems": [ { "lastUpdateAt": 1760541000, "objectId": "D_Head01", "quantity": 1 }, … ]
}
```

## Requirements

* Windows (native file dialog, Oodle DLL).
* .NET 10 SDK. A prebuilt `CUE4Parse.dll` ships in `libs\` (referenced via `HintPath` in `Wolves.csproj`).
* A `.usmap` mappings file for the installed game build.
