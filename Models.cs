namespace Wolves;

public static class Extras
{
    public static class Settings
    {
        public static string PakPath { get; set; } = @"C:\Program Files (x86)\Steam\steamapps\common\Dead by Daylight\DeadByDaylight\Content\Paks";
        public static string AesKey { get; set; } = "0x22B1639B548124925CF7B9CBAA09F9AC295FCF0324586D6B37EE1D42670B39B3";
        public static string MappingsPath { get; set; } = "";
    }
}

public static class Classes
{
    public static class FilePaths
    {
        public static List<string> ItemDb { get; } = [];
        public static List<string> OfferingDb { get; } = [];
        public static List<string> ItemAddonDb { get; } = [];
        public static List<string> PerkDb { get; } = [];
        public static List<string> CharacterDb { get; } = [];

        public static void Clear()
        {
            ItemDb.Clear();
            OfferingDb.Clear();
            ItemAddonDb.Clear();
            PerkDb.Clear();
            CharacterDb.Clear();
        }
    }

    public class Base
    {
        public string Role { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    public class Perk : Base
    {
        public int Character { get; set; }
    }

    public class Character : Base
    {
        /// <summary>
        /// The internal character identifier used by the backend (e.g. "Dwight", "Chuckles", "Bear").
        /// This is the value written to <c>characterName</c> in GetAll.json.
        /// </summary>
        public string CharacterId { get; set; } = string.Empty;
        public string Gender { get; set; } = string.Empty;
        public string ParentItem { get; set; } = string.Empty;
        public string DLC { get; set; } = string.Empty;
        public string Difficulty { get; set; } = string.Empty;
        public string BackStory { get; set; } = string.Empty;
        public string Biography { get; set; } = string.Empty;
        public string IconFilePath { get; set; } = string.Empty;
        public string BackgroundImagePath { get; set; } = string.Empty;
    }

    public static class Ids
    {
        public static Dictionary<string, Base> ItemIds { get; } = new();
        public static Dictionary<string, Base> ItemAddonIds { get; } = new();
        public static Dictionary<string, Base> OfferingIds { get; } = new();
        public static Dictionary<string, Perk> PerkIds { get; } = new();
        public static Dictionary<string, Character> CharacterIds { get; } = new();
    }
}
