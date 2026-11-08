using System;
using System.Collections.Generic;
using System.Globalization;
using A2G.Convert.Fm;

namespace A2G.Convert.Engine
{
    /// <summary>
    /// Skin and hair of a player. An FM save has no appearance data (neither fmsave nor the save file has skin, hair or face fields), so the
    /// defaults are a rough rule: the skin follows the player's nationality (a share of light, medium and dark skin per nation, the same
    /// player always gets the same result), the hair follows the skin and age. The user can set both for single players (<see cref="AppearanceOverride"/>).
    /// Skin: 0 light, 1 medium, 2 dark. Hair: 0 very light blond, 1 golden blond, 2 brown, 3 red, 4 black, 5 bald / shaved (checked in the game).
    /// </summary>
    public static class PlayerLook
    {
        public const int SkinCount = 3, HairCount = 6;

        // FM nation name -> percent of light, medium and dark skin (nations not listed: all light)
        private static readonly Dictionary<string, int[]> SkinShare = BuildTable();

        private static Dictionary<string, int[]> BuildTable()
        {
            var t = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);
            Action<int, int, int, string[]> add = (light, medium, dark, nations) => { foreach (var n in nations) t[n] = new[] { light, medium, dark }; };
            // mostly dark
            add(0, 5, 95, new[] { "Ghana", "Nigeria", "Cameroon", "Senegal", "Liberia", "DR Congo", "Democratic Republic of Congo", "Congo", "Ivory Coast", "Côte d'Ivoire", "Cote d'Ivoire",
                "Mali", "Guinea", "Guinea-Bissau", "Gabon", "Angola", "Sierra Leone", "Togo", "Benin", "Burkina Faso", "Uganda", "Kenya", "Tanzania", "Zambia", "Zimbabwe", "Mozambique",
                "Gambia", "Cape Verde", "Central African Republic", "Equatorial Guinea", "Sudan", "South Sudan", "Namibia", "Botswana", "Malawi", "Rwanda", "Burundi", "Ethiopia", "Haiti",
                "Jamaica", "Trinidad and Tobago", "Curaçao", "Suriname", "Grenada", "Barbados", "Antigua and Barbuda", "Saint Kitts and Nevis" });
            add(0, 25, 75, new[] { "South Africa", "Mauritania", "Chad", "Niger", "Comoros", "Lesotho", "Eswatini" });
            // mostly medium
            add(5, 80, 15, new[] { "Egypt", "Morocco", "Algeria", "Tunisia", "Libya", "Saudi Arabia", "Iran", "Iraq", "Syria", "Lebanon", "Jordan", "Qatar", "Bahrain", "Kuwait", "Oman",
                "United Arab Emirates", "Yemen", "Palestine", "India", "Pakistan", "Bangladesh", "Sri Lanka", "Nepal", "Indonesia", "Malaysia", "Philippines", "Thailand", "Vietnam",
                "Honduras", "Guatemala", "El Salvador", "Nicaragua", "Panama", "Dominican Republic", "Cuba", "Puerto Rico", "Bolivia", "Peru", "Ecuador", "Paraguay", "Venezuela",
                "Colombia", "Mexico", "Costa Rica", "Guyana", "Belize" });
            add(35, 60, 5, new[] { "Turkey", "Türkiye", "Israel", "Cyprus", "Malta", "Georgia", "Armenia", "Azerbaijan", "Uzbekistan", "Kazakhstan", "Kyrgyzstan", "Tajikistan",
                "Turkmenistan", "Afghanistan" });
            // mixed
            add(35, 40, 25, new[] { "Brazil" });
            add(60, 30, 10, new[] { "Argentina", "Chile" });
            add(55, 35, 10, new[] { "Uruguay" });
            add(60, 25, 15, new[] { "France" });
            add(75, 15, 10, new[] { "England", "Netherlands", "Belgium", "Portugal", "Wales", "Scotland", "Northern Ireland", "Republic of Ireland" });
            add(60, 15, 25, new[] { "United States", "USA", "Canada" });
            add(85, 10, 5, new[] { "Spain", "Italy", "Germany", "Switzerland", "Austria", "Sweden", "Denmark", "Norway", "Finland", "Greece", "Australia", "New Zealand" });
            add(90, 10, 0, new[] { "Japan", "South Korea", "Korea Republic", "China", "China PR", "Hong Kong", "Taiwan", "Mongolia" });
            return t;
        }

        private static int Hash(string uid, int salt)
        {
            unchecked
            {
                var h = 17 + salt * 7919;
                foreach (var c in uid ?? string.Empty) h = h * 31 + c;
                h ^= h >> 13; h *= 1274126177; h ^= h >> 16;
                return h & 0x7FFFFFFF;
            }
        }

        /// <summary>The default skin: the player's nationality, else the second nationality is ignored; light when the nation is unknown.</summary>
        public static int DefaultSkin(ConversionSettings settings, FmDataSet fm, FmPlayer p)
        {
            if (settings != null && !settings.SkinFromNationality) return 0;
            var nation = fm == null ? null : fm.NationBySaveId(p.NationId);
            int[] share;
            if (nation == null || !SkinShare.TryGetValue(nation.Name, out share)) return 0;
            var roll = Hash(p.Uid, 1) % 100;
            return roll < share[0] ? 0 : roll < share[0] + share[1] ? 1 : 2;
        }

        /// <summary>
        /// The default hair: black for dark skin, mostly black or brown for medium skin, all colours for light skin; a few bald older players.
        /// (The game has no grey hair: 0 very light blond, 1 golden blond, 2 brown, 3 red, 4 black, 5 bald.)
        /// </summary>
        public static int DefaultHair(FmPlayer p, int skin)
        {
            var roll = Hash(p.Uid, 2) % 100;
            if (p.Age >= 35 && roll < 12) return 5;                         // bald
            if (skin == 2) return roll < 92 ? 4 : 2;
            if (skin == 1) return roll < 60 ? 4 : roll < 95 ? 2 : 1;
            // light skin: black 16, brown 42, golden blond 22, very light blond 10, red 5, bald 5
            return roll < 16 ? 4 : roll < 58 ? 2 : roll < 80 ? 1 : roll < 90 ? 0 : roll < 95 ? 3 : 5;
        }

        public static int Skin(ConversionSettings settings, FmDataSet fm, FmPlayer p)
        {
            var o = settings == null ? null : settings.FindLook(p.Uid);
            return o != null && o.Skin >= 0 && o.Skin < SkinCount ? o.Skin : DefaultSkin(settings, fm, p);
        }

        public static int Hair(ConversionSettings settings, FmDataSet fm, FmPlayer p)
        {
            var o = settings == null ? null : settings.FindLook(p.Uid);
            if (o != null && o.Hair >= 0 && o.Hair < HairCount) return o.Hair;
            return DefaultHair(p, Skin(settings, fm, p));   // the hair default follows the skin that is in effect
        }

        /// <summary>Stores the user's choice for a player; values equal to the defaults are not stored.</summary>
        public static void SetOverride(ConversionSettings settings, FmDataSet fm, FmPlayer p, int skin, int hair)
        {
            var o = settings.FindLook(p.Uid);
            if (o != null) settings.PlayerLooks.Remove(o);
            var defaultSkin = DefaultSkin(settings, fm, p);
            var skinOverride = skin == defaultSkin ? -1 : skin;
            var hairOverride = hair == DefaultHair(p, skin) ? -1 : hair;
            if (skinOverride < 0 && hairOverride < 0) return;
            settings.PlayerLooks.Add(new AppearanceOverride { Uid = p.Uid, Skin = skinOverride, Hair = hairOverride });
        }

        public static void ClearOverride(ConversionSettings settings, FmPlayer p)
        {
            var o = settings.FindLook(p.Uid);
            if (o != null) settings.PlayerLooks.Remove(o);
        }
    }
}
