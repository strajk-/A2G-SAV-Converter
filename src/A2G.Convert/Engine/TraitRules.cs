using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using A2G.Convert.Fm;

namespace A2G.Convert.Engine
{
    /// <summary>One condition of a trait rule: an FM value between <see cref="Min"/> and <see cref="Max"/> (0 = no limit).</summary>
    [DataContract]
    public sealed class TraitCondition
    {
        /// <summary>An FM attribute / personality name (e.g. "leadership") or one of the pseudo values in <see cref="TraitEngine"/>.</summary>
        [DataMember] public string Attribute { get; set; }
        [DataMember] public int Min { get; set; }
        [DataMember] public int Max { get; set; }

        public TraitCondition() { }
        public TraitCondition(string attribute, int min, int max) { Attribute = attribute; Min = min; Max = max; }
    }

    /// <summary>
    /// An Anstoss 2 trait, character or health state and the FM values that earn it. Bits are the values of the
    /// .sav columns (verified from the game, see docs\Dev\sav-format-verified.md).
    /// </summary>
    [DataContract]
    public sealed class TraitRule
    {
        public const string KindTrait = "trait";
        /// <summary>The Talent flag (bit 2048 of the trait column): it comes on top of the one allowed trait and is never given to goalkeepers.</summary>
        public const string KindTalent = "talent";
        public const string KindCharacter = "character";
        public const string KindHealth = "health";

        /// <summary>ASCII identifier (also the key for the display name).</summary>
        [DataMember] public string Name { get; set; }
        [DataMember] public string Kind { get; set; }
        [DataMember] public int Bit { get; set; }
        [DataMember] public bool Enabled { get; set; }
        [DataMember] public List<TraitCondition> Conditions { get; set; }

        public TraitRule() { Enabled = true; Conditions = new List<TraitCondition>(); }

        public TraitRule(string name, string kind, int bit, params TraitCondition[] conditions)
        {
            Name = name; Kind = kind; Bit = bit; Enabled = true; Conditions = conditions.ToList();
        }
    }

    /// <summary>Evaluates the trait rules for FM players.</summary>
    public static class TraitEngine
    {
        public const string Age = "age";
        public const string CurrentAbility = "current_ability";
        public const string PotentialAbility = "potential_ability";
        /// <summary>Potential minus current ability (how much a player can still grow).</summary>
        public const string GrowthRoom = "growth_room";

        public static readonly string[] PseudoValues = { Age, CurrentAbility, PotentialAbility, GrowthRoom };

        /// <summary>FM personality values (hidden attributes); they come from the save next to the visible attributes.</summary>
        public static readonly string[] PersonalityValues =
        {
            "adaptability", "ambition", "loyalty", "pressure", "professionalism", "sportsmanship", "temperament", "controversy"
        };

        /// <summary>The game only shows the first five traits (bits 1..16) for goalkeepers.</summary>
        public const int GoalkeeperTraitLimit = 16;

        /// <summary>Pairs that exclude each other when a player may have more than one trait.</summary>
        private static readonly string[][] Exclusive =
        {
            new[] { "Trainingsweltmeister", "Trainingsfaul" }, new[] { "Treter", "FairerSpieler" },
            new[] { "Allrounder", "FlexiblerSpieler" }, new[] { "Heimspieler", "Auswaertsspieler" }
        };

        /// <summary>Scale of a value, used to compare how far a player is beyond a threshold across different conditions.</summary>
        private static double Scale(string attribute)
        {
            switch (attribute)
            {
                case CurrentAbility: case PotentialAbility: return 200;
                case GrowthRoom: return 100;
                case Age: return 50;
                default: return 20;
            }
        }

        /// <summary>The value of an attribute for a player, or null when the save does not have it.</summary>
        public static int? Value(FmPlayer p, string attribute)
        {
            switch (attribute)
            {
                case Age: return p.Age;
                case CurrentAbility: return p.CurrentAbility;
                case PotentialAbility: return p.PotentialAbility;
                case GrowthRoom: return p.PotentialAbility - p.CurrentAbility;
            }
            int v;
            if (p.Attributes != null && p.Attributes.TryGetValue(attribute, out v)) return v;
            return null;
        }

        /// <summary>True when all conditions hold; margin says how far beyond the thresholds the player is (0..1, for ranking).</summary>
        public static bool Matches(TraitRule rule, FmPlayer p, out double margin)
        {
            margin = 0;
            if (rule.Conditions == null || rule.Conditions.Count == 0) return false;
            double sum = 0;
            foreach (var c in rule.Conditions)
            {
                var value = Value(p, c.Attribute);
                if (value == null) return false;
                double exceed = 0;
                if (c.Min > 0) { if (value.Value < c.Min) return false; exceed += value.Value - c.Min; }
                if (c.Max > 0) { if (value.Value > c.Max) return false; exceed += c.Max - value.Value; }
                if (c.Min > 0 && c.Max > 0) exceed = Math.Min(value.Value - c.Min, c.Max - value.Value);
                sum += exceed / Scale(c.Attribute);
            }
            margin = sum / rule.Conditions.Count;
            return true;
        }

        /// <summary>How many of the given players satisfy a rule (used for the live counter in the options).</summary>
        public static int Count(TraitRule rule, IEnumerable<FmPlayer> players)
        {
            double m;
            return players.Count(p => Matches(rule, p, out m));
        }

        /// <summary>
        /// The trait column and the character + health column of a player. At most <see cref="ConversionSettings.MaxTraits"/>
        /// traits (best margin first, exclusive pairs respected) plus Talent, one character and one health state.
        /// </summary>
        public static void Assign(ConversionSettings settings, FmPlayer p, bool keeper, out int traits, out int characterHealth)
        {
            traits = 0;
            characterHealth = 0;
            if (settings.TraitRules == null) return;

            var hits = new List<KeyValuePair<double, TraitRule>>();
            foreach (var rule in settings.TraitRules.Where(r => r.Enabled))
            {
                double margin;
                if (Matches(rule, p, out margin)) hits.Add(new KeyValuePair<double, TraitRule>(margin, rule));
            }
            var ordered = hits.OrderByDescending(h => h.Key).Select(h => h.Value).ToList();

            var picked = new List<TraitRule>();
            foreach (var rule in ordered.Where(r => r.Kind == TraitRule.KindTrait))
            {
                if (picked.Count >= Math.Max(1, settings.MaxTraits)) break;
                if (keeper && rule.Bit > GoalkeeperTraitLimit) continue;
                if (picked.Any(x => AreExclusive(x.Name, rule.Name))) continue;
                picked.Add(rule);
            }
            foreach (var rule in picked) traits |= rule.Bit;
            if (!keeper)
            {
                var talent = ordered.FirstOrDefault(r => r.Kind == TraitRule.KindTalent);
                if (talent != null) traits |= talent.Bit;
            }

            var character = ordered.FirstOrDefault(r => r.Kind == TraitRule.KindCharacter);
            if (character != null) characterHealth |= character.Bit;
            var health = ordered.FirstOrDefault(r => r.Kind == TraitRule.KindHealth);
            if (health != null) characterHealth |= health.Bit;
        }

        private static bool AreExclusive(string a, string b)
        {
            return Exclusive.Any(pair => (pair[0] == a && pair[1] == b) || (pair[0] == b && pair[1] == a));
        }

        /// <summary>The names of the original game (German), by rule name.</summary>
        public static string DisplayName(string name)
        {
            string text;
            return GameNames.TryGetValue(name, out text) ? text : name;
        }

        private static readonly Dictionary<string, string> GameNames = new Dictionary<string, string>
        {
            { "Fuehrungsperson", "Führungsperson" }, { "Kaempfernatur", "Kämpfernatur" }, { "Trainingsweltmeister", "Trainingsweltmeister" },
            { "Trainingsfaul", "Trainingsfaul" }, { "Treter", "Treter" }, { "FairerSpieler", "Fairer Spieler" },
            { "Schwalbenkoenig", "Schwalbenkönig" }, { "Allrounder", "Allrounder" }, { "FlexiblerSpieler", "Flexibler Spieler" },
            { "Heimspieler", "Heimspieler" }, { "Auswaertsspieler", "Auswärtsspieler" }, { "Talent", "Talent" },
            { "Hitzkopf", "Hitzkopf" }, { "Frohnatur", "Frohnatur" }, { "MannFuerGrosses", "Mann für Großes" },
            { "Nervenbuendel", "Nervenbündel" }, { "Phlegma", "Phlegma" }, { "Geldgeier", "Geldgeier" },
            { "Musterprofi", "Musterprofi" }, { "Skandalnudel", "Skandalnudel" },
            { "Wegstecken", "Kann einiges wegstecken" }, { "Verletzungsanfaellig", "Verletzungsanfällig" }, { "Knieprobleme", "Knieprobleme" }
        };

        /// <summary>
        /// The default rules. Bits are the values stored in the .sav files: traits 1 &lt;&lt; index, characters 2 &lt;&lt; index,
        /// health states 512 / 1024 / 2048 (see docs\Dev\sav-format-verified.md).
        /// </summary>
        public static List<TraitRule> DefaultRules()
        {
            Func<string, int, int, TraitCondition> c = (a, min, max) => new TraitCondition(a, min, max);
            Func<string, int, TraitCondition> atLeast = (a, min) => new TraitCondition(a, min, 0);
            Func<string, int, TraitCondition> atMost = (a, max) => new TraitCondition(a, 0, max);
            return new List<TraitRule>
            {
                new TraitRule("Fuehrungsperson", TraitRule.KindTrait, 1, atLeast("leadership", 17)),
                new TraitRule("Kaempfernatur", TraitRule.KindTrait, 2, atLeast("determination", 16), atLeast("work_rate", 14)),
                new TraitRule("Trainingsweltmeister", TraitRule.KindTrait, 4, atLeast("professionalism", 15), atLeast("work_rate", 15)),
                new TraitRule("Trainingsfaul", TraitRule.KindTrait, 8, atMost("professionalism", 6)),
                new TraitRule("Treter", TraitRule.KindTrait, 16, atMost("sportsmanship", 6), atLeast("aggression", 14)),
                new TraitRule("FairerSpieler", TraitRule.KindTrait, 32, atLeast("sportsmanship", 16)),
                new TraitRule("Schwalbenkoenig", TraitRule.KindTrait, 64, atMost("sportsmanship", 6), atLeast("controversy", 12)),
                new TraitRule("Allrounder", TraitRule.KindTrait, 128, atLeast("versatility", 18)),
                new TraitRule("FlexiblerSpieler", TraitRule.KindTrait, 256, c("versatility", 16, 17)),
                new TraitRule("Heimspieler", TraitRule.KindTrait, 512, atLeast("loyalty", 16), atMost("adaptability", 10)),
                new TraitRule("Auswaertsspieler", TraitRule.KindTrait, 1024, atLeast("adaptability", 17), atLeast("pressure", 12), atMost("loyalty", 10)),
                new TraitRule("Talent", TraitRule.KindTalent, 2048, atMost(Age, 23), atLeast(GrowthRoom, 30), atLeast(PotentialAbility, 140)),

                new TraitRule("Hitzkopf", TraitRule.KindCharacter, 2, atMost("temperament", 6)),
                new TraitRule("Frohnatur", TraitRule.KindCharacter, 4, atLeast("temperament", 17), atLeast("teamwork", 14)),
                new TraitRule("MannFuerGrosses", TraitRule.KindCharacter, 8, atLeast("pressure", 16), atLeast("important_matches", 15)),
                new TraitRule("Nervenbuendel", TraitRule.KindCharacter, 16, atMost("pressure", 6)),
                new TraitRule("Phlegma", TraitRule.KindCharacter, 32, atMost("ambition", 7), atMost("determination", 10)),
                new TraitRule("Geldgeier", TraitRule.KindCharacter, 64, atMost("professionalism", 10), atMost("sportsmanship", 10), atLeast("determination", 15), atMost("loyalty", 10)),
                new TraitRule("Musterprofi", TraitRule.KindCharacter, 128, atLeast("professionalism", 16), atLeast(PotentialAbility, 170)),
                new TraitRule("Skandalnudel", TraitRule.KindCharacter, 256, atLeast("controversy", 16)),

                new TraitRule("Wegstecken", TraitRule.KindHealth, 512, atLeast("natural_fitness", 17), atMost("injury_proneness", 5)),
                new TraitRule("Verletzungsanfaellig", TraitRule.KindHealth, 1024, c("injury_proneness", 16, 17)),
                new TraitRule("Knieprobleme", TraitRule.KindHealth, 2048, atLeast("injury_proneness", 18))
            };
        }
    }
}
