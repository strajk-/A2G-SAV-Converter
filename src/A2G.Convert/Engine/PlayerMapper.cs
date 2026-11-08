using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using A2G.Convert.Fm;
using A2G.Sav;

namespace A2G.Convert.Engine
{
    /// <summary>Anstoss 2 text fields are Latin-1 and short: players 16 / 9 characters, clubs about 20.</summary>
    public static class NameShortener
    {
        /// <summary>
        /// The editor allows 16 characters, but the game shows a 16-character surname with garbage after it (found with "Gabriel Moscardo");
        /// the shipped player data never goes beyond 15, so 15 is the limit.
        /// </summary>
        public const int MaxLastName = 15;
        public const int MaxFirstName = 9;

        /// <summary>Replaces characters outside Latin-1 by their base letter and drops what cannot be represented.</summary>
        public static string Latin1Safe(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            var sb = new StringBuilder(text.Length);
            foreach (var ch in text.Normalize(NormalizationForm.FormC))
            {
                if (ch < 0x100 && ch >= 0x20) { sb.Append(ch); continue; }
                switch (ch)
                {
                    case 'Ł': sb.Append('L'); continue;
                    case 'ł': sb.Append('l'); continue;
                    case 'Đ': sb.Append('D'); continue;
                    case 'đ': sb.Append('d'); continue;
                    case 'ı': sb.Append('i'); continue;
                    case '’': case '‘': sb.Append('\''); continue;
                }
                foreach (var d in ch.ToString().Normalize(NormalizationForm.FormD))
                {
                    if (CharUnicodeInfo.GetUnicodeCategory(d) == UnicodeCategory.NonSpacingMark) continue;
                    if (d < 0x100 && d >= 0x20) sb.Append(d);
                }
            }
            return sb.ToString();
        }

        /// <summary>Cuts to <paramref name="max"/> characters, marking the cut with a dot like the original data ("Maximil.").</summary>
        public static string Fit(string text, int max)
        {
            var t = Latin1Safe(text).Trim();
            while (t.Contains("  ")) t = t.Replace("  ", " ");
            if (t.Length <= max) return t;
            return t.Substring(0, max - 1).TrimEnd() + ".";
        }
    }

    /// <summary>Fits FM club names into the text fields of the Land / Avereine / Stad files.</summary>
    public static class ClubNamer
    {
        // Maximum lengths seen in the original files.
        public const int MaxName = 19;
        public const int MaxCity = 16;
        public const int MaxNickname = 18;
        public const int MaxStadium = 26;
        public const int MaxAvereineShortName = 12;

        /// <summary>The full name if it fits, else FM's short name if it fits, else the full name cut with a dot.</summary>
        public static string ClubName(Fm.FmClub club)
        {
            var name = NameShortener.Fit(club.Name, int.MaxValue);
            if (name.Length <= MaxName) return name;
            var shortName = NameShortener.Fit(club.ShortName ?? string.Empty, int.MaxValue);
            if (shortName.Length > 0 && shortName.Length <= MaxName) return shortName;
            return NameShortener.Fit(name, MaxName);
        }
    }

    /// <summary>Maps FM nations to the nation indexes of LAENDER.SAV by name. Unknown nations become "Sonstige" (0).</summary>
    public sealed class A2Nations
    {
        // A2 name (German) -> FM names (English) that mean the same nation
        private static readonly string[][] Aliases =
        {
            new[] { "Ägypten", "Egypt" }, new[] { "Albanien", "Albania" }, new[] { "Algerien", "Algeria" },
            new[] { "Argentinien", "Argentina" }, new[] { "Australien", "Australia" }, new[] { "Belgien", "Belgium" },
            new[] { "Bolivien", "Bolivia" }, new[] { "Brasilien", "Brazil" }, new[] { "Bulgarien", "Bulgaria" },
            new[] { "Chile", "Chile" }, new[] { "China", "China PR", "China" }, new[] { "Costa Rica", "Costa Rica" },
            new[] { "Dänemark", "Denmark" }, new[] { "Deutschland", "Germany" }, new[] { "Ecuador", "Ecuador" },
            new[] { "England", "England" }, new[] { "Estland", "Estonia" }, new[] { "Färöer Inseln", "Faroe Islands" },
            new[] { "Finnland", "Finland" }, new[] { "Frankreich", "France" }, new[] { "Georgien", "Georgia" },
            new[] { "Ghana", "Ghana" }, new[] { "Griechenland", "Greece" }, new[] { "Holland", "Netherlands" },
            new[] { "Irland", "Republic of Ireland", "Ireland" }, new[] { "Island", "Iceland" }, new[] { "Israel", "Israel" },
            new[] { "Italien", "Italy" }, new[] { "Jamaika", "Jamaica" }, new[] { "Japan", "Japan" },
            new[] { "Kamerun", "Cameroon" }, new[] { "Kanada", "Canada" }, new[] { "Kolumbien", "Colombia" },
            new[] { "Kroatien", "Croatia" }, new[] { "Lettland", "Latvia" }, new[] { "Liberia", "Liberia" },
            new[] { "Litauen", "Lithuania" }, new[] { "Luxemburg", "Luxembourg" }, new[] { "Malta", "Malta" },
            new[] { "Marokko", "Morocco" }, new[] { "Mexiko", "Mexico" }, new[] { "Moldawien", "Moldova" },
            new[] { "Neuseeland", "New Zealand" }, new[] { "Nigeria", "Nigeria" }, new[] { "Nordirland", "Northern Ireland" },
            new[] { "Norwegen", "Norway" }, new[] { "Österreich", "Austria" }, new[] { "Paraguay", "Paraguay" },
            new[] { "Peru", "Peru" }, new[] { "Polen", "Poland" }, new[] { "Portugal", "Portugal" },
            new[] { "Puerto Rico", "Puerto Rico" }, new[] { "Rumänien", "Romania" }, new[] { "Rußland", "Russia" },
            new[] { "San Marino", "San Marino" }, new[] { "Saudi-Arabien", "Saudi Arabia" },
            new[] { "Schottland", "Scotland" }, new[] { "Schweden", "Sweden" }, new[] { "Schweiz", "Switzerland" },
            new[] { "Senegal", "Senegal" }, new[] { "Slowakei", "Slovakia" }, new[] { "Slowenien", "Slovenia" },
            new[] { "Spanien", "Spain" }, new[] { "Südkorea", "Korea Republic", "South Korea" },
            new[] { "Tschechien", "Czech Republic", "Czechia" }, new[] { "Tunesien", "Tunisia" },
            new[] { "Türkei", "Turkey", "Türkiye" }, new[] { "Ukraine", "Ukraine" }, new[] { "Ungarn", "Hungary" },
            new[] { "Uruguay", "Uruguay" }, new[] { "USA", "United States", "USA" }, new[] { "Venezuela", "Venezuela" },
            new[] { "Wales", "Wales" }, new[] { "Zaire", "Democratic Republic of Congo", "DR Congo", "Zaire" },
            new[] { "Zypern", "Cyprus" }
        };

        private readonly Dictionary<string, int> _fmNameToIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        /// <summary>a2NationNames: the names of LAENDER.SAV in file order (index = nation id).</summary>
        public A2Nations(IList<string> a2NationNames)
        {
            foreach (var alias in Aliases)
            {
                var index = -1;
                for (var i = 0; i < a2NationNames.Count; i++)
                    if (string.Equals(a2NationNames[i], alias[0], StringComparison.Ordinal)) { index = i; break; }
                if (index < 0) continue;
                for (var k = 1; k < alias.Length; k++) _fmNameToIndex[alias[k]] = index;
            }
        }

        /// <summary>The FM names of the nations behind an Anstoss 2 nation name (e.g. "Holland" -> "Netherlands").</summary>
        public static IEnumerable<string> FmNamesOf(string a2NationName)
        {
            foreach (var alias in Aliases)
                if (string.Equals(alias[0], a2NationName, StringComparison.Ordinal))
                    return alias.Skip(1);
            return new string[0];
        }

        public int Resolve(string fmNationName)
        {
            int index;
            return fmNationName != null && _fmNameToIndex.TryGetValue(fmNationName, out index) ? index : 0;
        }
    }

    /// <summary>Converts one FM player into an Anstoss 2 player record using the user's <see cref="ConversionSettings"/>.</summary>
    public sealed class PlayerMapper
    {
        // A2 position codes: 1 GK, 2 libero, 3 Manndecker, 4 left back, 5 right back, 6 defensive MF, 7 left MF,
        // 8 right MF, 9 offensive MF, 10 striker. When scores tie the earlier entry wins.
        private readonly ConversionSettings _settings;
        private readonly FmDataSet _fm;
        private readonly A2Nations _nations;

        public ConversionSettings Settings { get { return _settings; } }

        public PlayerMapper(ConversionSettings settings, FmDataSet fm, A2Nations nations)
        {
            _settings = settings;
            _fm = fm;
            _nations = nations;
        }

        public Player Map(FmPlayer p)
        {
            var result = new Player();
            string last = !string.IsNullOrEmpty(p.CommonName) ? p.CommonName
                : !string.IsNullOrEmpty(p.LastName) ? p.LastName : p.DisplayName;
            string first = p.FirstName;
            if (!string.IsNullOrEmpty(p.CommonName) && !string.IsNullOrEmpty(first))
            {
                // A common name of several words that starts with the first name ("Gabriel Moscardo", "Samu Fernández") already holds
                // both parts: first name and surname are split from it instead of repeating the first name as "G.".
                var parts = p.CommonName.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                var firstWord = first.Trim().Split(' ')[0];
                if (parts.Length >= 2 && firstWord.Length > 0 &&
                    (firstWord.StartsWith(parts[0], StringComparison.OrdinalIgnoreCase) || parts[0].StartsWith(firstWord, StringComparison.OrdinalIgnoreCase)))
                {
                    first = parts[0];
                    last = string.Join(" ", parts.Skip(1));
                }
                else
                    first = first.Substring(0, 1) + ".";   // single-name players keep the common name as surname
            }
            if (string.IsNullOrWhiteSpace(first)) first = "-";

            result.LastName = NameShortener.Fit(last, NameShortener.MaxLastName);
            result.FirstName = NameShortener.Fit(first, NameShortener.MaxFirstName);
            if (result.LastName.Length == 0) result.LastName = "-";
            if (result.FirstName.Length == 0) result.FirstName = "-";

            result.Unknown0 = 0;
            result.Skin = PlayerLook.Skin(_settings, _fm, p);   // FM saves have no appearance data: nationality rule, or the user's choice
            result.Hair = PlayerLook.Hair(_settings, _fm, p);
            result.Age = Math.Max(17, Math.Min(50, p.Age));
            result.Strength = StrengthFor(p.CurrentAbility);
            var nation = _fm.NationBySaveId(p.NationId);
            result.Nationality = _nations.Resolve(nation != null ? nation.Name : null);

            SetPositions(p, result);
            SetAbilities(p, result);
            int traits, characterHealth;
            TraitEngine.Assign(_settings, p, result.Position1 == 1, out traits, out characterHealth);
            result.Traits = traits;
            result.CharacterHealth = characterHealth;
            return result;
        }

        /// <summary>1..7 from FM Current Ability; 8 is a random in-game event and is never written.</summary>
        public int StrengthFor(int currentAbility)
        {
            var bands = _settings.StrengthBands;
            var strength = 1;
            for (var i = 0; i < bands.Length && i < 6; i++)
                if (currentAbility >= bands[i]) strength = i + 2;
            return Math.Min(7, strength);
        }

        private static int HairFor(string uid)
        {
            int n;
            return int.TryParse(uid, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) ? Math.Abs(n) % 6 : 0;
        }

        private void SetPositions(FmPlayer p, Player result)
        {
            var scores = PositionRules.Scores(p);
            var primary = PositionRules.Primary(scores);
            result.Position1 = primary;
            var secondary = PositionRules.Secondary(scores, primary, _settings.SecondaryPositionMinRating);
            result.Position2 = secondary.Count > 0 ? secondary[0] : 0;
            result.Position3 = secondary.Count > 1 ? secondary[1] : 0;
        }
        private void SetAbilities(FmPlayer p, Player result)
        {
            result.PositiveSkills = 0;
            result.NegativeSkills = 0;
            bool keeper = result.Position1 == 1;   // goalkeepers only get goalkeeper abilities, everybody else only field abilities

            var positive = new List<KeyValuePair<double, int>>();
            var negative = new List<KeyValuePair<double, int>>();
            foreach (var rule in _settings.AbilityRules.Where(x => x.Enabled && x.Goalkeeper == keeper && x.Attributes != null && x.Attributes.Length > 0))
            {
                var values = rule.Attributes.Select(a => (double)p.Attribute(a)).ToList();
                var value = rule.Mode == "min" ? values.Min() : rule.Mode == "max" ? values.Max() : values.Average();
                if (value >= rule.PositiveAtLeast) positive.Add(new KeyValuePair<double, int>(value - rule.PositiveAtLeast, rule.Bit));
                else if (rule.NegativeAtMost > 0 && value <= rule.NegativeAtMost)
                    negative.Add(new KeyValuePair<double, int>(rule.NegativeAtMost - value, rule.Bit));
            }
            foreach (var x in positive.OrderByDescending(x => x.Key).Take(_settings.MaxPositiveAbilities)) result.PositiveSkills |= x.Value;
            foreach (var x in negative.OrderByDescending(x => x.Key).Take(_settings.MaxNegativeAbilities))
                if ((result.PositiveSkills & x.Value) == 0) result.NegativeSkills |= x.Value;
        }
    }
}
