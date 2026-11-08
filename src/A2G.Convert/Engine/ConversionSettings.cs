using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace A2G.Convert.Engine
{
    /// <summary>
    /// One FM-attribute rule that grants (or removes) an Anstoss 2 ability. The attributes are combined
    /// ("avg", "min" or "max"); a value of at least <see cref="PositiveAtLeast"/> grants the ability, a value of at most
    /// <see cref="NegativeAtMost"/> (when above 0) gives the negative variant. FM attributes are scaled 1..20.
    /// </summary>
    [DataContract]
    public sealed class AbilityRule
    {
        [DataMember] public string Name { get; set; }
        /// <summary>Bit value in the PositiveSkills / NegativeSkills column.</summary>
        [DataMember] public int Bit { get; set; }
        [DataMember] public bool Enabled { get; set; }
        /// <summary>True for goalkeeper abilities: only goalkeepers get these, and goalkeepers get nothing else.</summary>
        [DataMember] public bool Goalkeeper { get; set; }
        [DataMember] public string[] Attributes { get; set; }
        [DataMember] public string Mode { get; set; }
        [DataMember] public double PositiveAtLeast { get; set; }
        /// <summary>0 disables the negative variant.</summary>
        [DataMember] public double NegativeAtMost { get; set; }

        public AbilityRule() { Enabled = true; Mode = "avg"; }

        public AbilityRule(string name, int bit, string mode, double positive, double negative, params string[] attributes)
        {
            Name = name; Bit = bit; Enabled = true; Mode = mode;
            PositiveAtLeast = positive; NegativeAtMost = negative; Attributes = attributes;
        }
    }

    /// <summary>
    /// Texts and the stadium size the FM save does not contain, changed by the user for one club (city, nickname, stadium name,
    /// capacity). An empty text / a capacity of 0 means the generated default is used.
    /// </summary>
    [DataContract]
    public sealed class ClubInfoOverride
    {
        /// <summary>The FM club id.</summary>
        [DataMember] public string Uid { get; set; }
        [DataMember] public string City { get; set; }
        [DataMember] public string Nickname { get; set; }
        [DataMember] public string Stadium { get; set; }
        /// <summary>Stadium capacity (spectators); 0 = the default derived from the club's reputation.</summary>
        [DataMember] public int Capacity { get; set; }
        /// <summary>The kit the user chose: home pattern, shirt, shorts, socks, then the same for away (null = the default kit).</summary>
        [DataMember] public int[] Kit { get; set; }
    }

    /// <summary>A club the user put into a slot of the last season's results (champion, UEFA place 2, promoted 1, ...) of a league.</summary>
    [DataContract]
    public sealed class SeasonSlotOverride
    {
        /// <summary>The league (its LIGA file) or "AVE:" plus the LAENDER index of the nation for non-league clubs.</summary>
        [DataMember] public string Group { get; set; }
        /// <summary>The slot name, see <see cref="SeasonResults"/>.</summary>
        [DataMember] public string Slot { get; set; }
        /// <summary>The FM club id.</summary>
        [DataMember] public string Uid { get; set; }
    }

    /// <summary>A player's skin or hair the user changed (the FM save has no appearance data); -1 = the default.</summary>
    [DataContract]
    public sealed class AppearanceOverride
    {
        /// <summary>The FM player id.</summary>
        [DataMember] public string Uid { get; set; }
        /// <summary>0 light, 1 medium, 2 dark; -1 = default.</summary>
        [DataMember] public int Skin { get; set; }
        /// <summary>0 very light blond, 1 golden blond, 2 brown, 3 red, 4 black, 5 bald; -1 = default.</summary>
        [DataMember] public int Hair { get; set; }
    }

    /// <summary>
    /// All tunable conversion settings (stored as mapping\settings.json in a project, edited in the Options screen).
    /// </summary>
    [DataContract]
    public sealed class ConversionSettings
    {
        /// <summary>A club needs at least this many real players in the FM save to fill a team slot.</summary>
        [DataMember] public int MinPlayersForTeam { get; set; }
        /// <summary>Anstoss 2 requires 16 players per team at game start; smaller squads are padded with the best free agents of the club's nation.</summary>
        [DataMember] public int TeamMinimum { get; set; }
        /// <summary>Squad size written for each team (Anstoss 2 allows 16..30).</summary>
        [DataMember] public int SquadSize { get; set; }
        /// <summary>
        /// Players wanted per Anstoss 2 position (index = position - 1: Torwart, Libero, Manndecker, linker / rechter Verteidiger,
        /// defensives Mittelfeld, linkes / rechtes Mittelfeld, offensives Mittelfeld, Stuermer). A club without a Libero gives
        /// that quota to an offensive midfielder; missing players are replaced along the substitution chains of <see cref="SquadSelector"/>.
        /// </summary>
        [DataMember] public int[] SquadQuota { get; set; }
        /// <summary>The same for the non-league (Avereine) clubs: exactly 11 players, one goalkeeper, no libero.</summary>
        [DataMember] public int[] AvereineQuota { get; set; }
        /// <summary>When the squad is larger than the quotas, the extra players are the best remaining ones, but at most this many goalkeepers...</summary>
        [DataMember] public int ExtraGoalkeeperCap { get; set; }
        /// <summary>... and at most this many strikers.</summary>
        [DataMember] public int ExtraStrikerCap { get; set; }
        /// <summary>
        /// How much of a young player's growth room (PA - CA) counts when squads are picked, in percent: the pick score is
        /// CA + weight x (PA - CA) x youth factor. 0 = current ability only; 50 lets a 100/170 talent rank like a 135 player.
        /// </summary>
        [DataMember] public int PotentialWeight { get; set; }
        /// <summary>Players up to this age count their full growth room ...</summary>
        [DataMember] public int PotentialFullUntilAge { get; set; }
        /// <summary>... fading out linearly until this age, from which only current ability counts.</summary>
        [DataMember] public int PotentialNoneFromAge { get; set; }
        [DataMember] public int MinGoalkeepers { get; set; }
        [DataMember] public int AvereineSquadSize { get; set; }
        [DataMember] public int AvereineClubCount { get; set; }
        [DataMember] public int AvereineExtraClubCount { get; set; }
        /// <summary>Maximum clubs of one nation among the non-league (Avereine) clubs.</summary>
        [DataMember] public int AvereinePerNationCap { get; set; }
        [DataMember] public int AvereineMinPlayers { get; set; }

        /// <summary>FM Current Ability (CA) thresholds for strength 1..7: CA below [0] = 1, below [1] = 2, ... at least [5] = 7. Strength is never 8.</summary>
        [DataMember] public int[] StrengthBands { get; set; }
        /// <summary>FM position rating (0..20) from which a position counts as a secondary position.</summary>
        [DataMember] public int SecondaryPositionMinRating { get; set; }
        /// <summary>Most positive abilities one player may get (Anstoss 2 allows two).</summary>
        [DataMember] public int MaxPositiveAbilities { get; set; }
        [DataMember] public int MaxNegativeAbilities { get; set; }
        [DataMember] public List<AbilityRule> AbilityRules { get; set; }
        /// <summary>Most traits one player may get (the Talent flag comes on top). Anstoss 2 players nearly always have only one.</summary>
        [DataMember] public int MaxTraits { get; set; }
        /// <summary>Traits, characters and health states earned from FM attributes and personality.</summary>
        [DataMember] public List<TraitRule> TraitRules { get; set; }
        /// <summary>Per-club texts and stadium capacity the user changed; they survive a rebuild because they are keyed on the FM club id.</summary>
        [DataMember] public List<ClubInfoOverride> ClubInfo { get; set; }
        /// <summary>The clubs the user put into slots of the last season's results (see <see cref="SeasonResults"/>).</summary>
        [DataMember] public List<SeasonSlotOverride> SeasonSlots { get; set; }

        public ClubInfoOverride FindClubInfo(string uid)
        {
            return ClubInfo == null ? null : ClubInfo.FirstOrDefault(o => o.Uid == uid);
        }

        /// <summary>The year the game starts in (1998-2098; the game's own start is 1998): 0 = the year of the FM save.</summary>
        [DataMember] public int StartYear { get; set; }

        /// <summary>Skin from the player's nationality (a rough rule, see <see cref="PlayerLook"/>); off = every player has the lightest skin.</summary>
        [DataMember] public bool SkinFromNationality { get; set; }
        /// <summary>Skin and hair the user set for single players, keyed on the FM player id.</summary>
        [DataMember] public List<AppearanceOverride> PlayerLooks { get; set; }

        public AppearanceOverride FindLook(string uid)
        {
            return PlayerLooks == null ? null : PlayerLooks.FirstOrDefault(o => o.Uid == uid);
        }

        /// <summary>The serializer does not run the constructor, so settings files from older versions get these defaults first.</summary>
        [OnDeserializing]
        private void SetDeserializingDefaults(StreamingContext context)
        {
            PotentialWeight = 50;
            PotentialFullUntilAge = 21;
            PotentialNoneFromAge = 27;
            SquadQuota = DefaultSquadQuota();
            AvereineQuota = DefaultAvereineQuota();
            ClubInfo = new List<ClubInfoOverride>();
            SeasonSlots = new List<SeasonSlotOverride>();
            PlayerLooks = new List<AppearanceOverride>();
            SkinFromNationality = true;
        }

        /// <summary>Rank of a player for squad picks: current ability plus the share of the growth room that counts at his age.</summary>
        public double PickScore(A2G.Convert.Fm.FmPlayer p)
        {
            double youth = p.Age <= PotentialFullUntilAge ? 1.0
                : p.Age >= PotentialNoneFromAge ? 0.0
                : (double)(PotentialNoneFromAge - p.Age) / Math.Max(1, PotentialNoneFromAge - PotentialFullUntilAge);
            return p.CurrentAbility + (PotentialWeight / 100.0) * Math.Max(0, p.PotentialAbility - p.CurrentAbility) * youth;
        }

        public ConversionSettings()
        {
            MinPlayersForTeam = 11;
            TeamMinimum = 16;
            SquadSize = 21;
            PotentialWeight = 50;
            PotentialFullUntilAge = 21;
            PotentialNoneFromAge = 27;
            SquadQuota = DefaultSquadQuota();
            AvereineQuota = DefaultAvereineQuota();
            ExtraGoalkeeperCap = 3;
            ExtraStrikerCap = 6;
            MinGoalkeepers = 2;
            AvereineSquadSize = 11;
            AvereineClubCount = 130;
            AvereineExtraClubCount = 40;
            AvereinePerNationCap = 6;
            AvereineMinPlayers = 11;
            StrengthBands = new[] { 80, 92, 104, 120, 145, 170 };
            SecondaryPositionMinRating = 15;
            MaxPositiveAbilities = 2;
            MaxNegativeAbilities = 1;
            AbilityRules = DefaultAbilityRules();
            MaxTraits = 1;
            TraitRules = TraitEngine.DefaultRules();
            ClubInfo = new List<ClubInfoOverride>();
            SeasonSlots = new List<SeasonSlotOverride>();
            PlayerLooks = new List<AppearanceOverride>();
            SkinFromNationality = true;
        }

        /// <summary>
        /// Ability bits follow the order of the original editor (bit = 2 &lt;&lt; index); see docs\Dev\sav-format-verified.md.
        /// Trait, character and health rules live in <see cref="TraitEngine.DefaultRules"/>.
        /// </summary>
        public static List<AbilityRule> DefaultAbilityRules()
        {
            return new List<AbilityRule>
            {
                new AbilityRule("Kopfball",       2,    "avg", 15, 6, "heading", "jumping_reach"),
                new AbilityRule("Zweikampf",      4,    "avg", 16, 6, "tackling"),
                new AbilityRule("Schnelligkeit",  8,    "avg", 17, 6, "pace", "acceleration"),
                new AbilityRule("Schusskraft",    16,   "avg", 16, 0, "long_shots"),
                new AbilityRule("Freistoss",      32,   "avg", 16, 0, "free_kick_taking"),
                new AbilityRule("Flanken",        64,   "avg", 16, 0, "crossing"),
                new AbilityRule("Torinstinkt",    128,  "avg", 16, 0, "finishing", "off_the_ball"),
                new AbilityRule("Laufstaerke",    256,  "avg", 16, 0, "stamina", "work_rate"),
                new AbilityRule("Technik",        512,  "avg", 16, 0, "technique", "first_touch", "dribbling"),
                new AbilityRule("Spielmacher",    1024, "avg", 16, 0, "vision", "passing"),
                new AbilityRule("Beidfuessigkeit", 2048, "min", 15, 0, "left_foot", "right_foot")
            }.Concat(DefaultGoalkeeperRules()).ToList();
        }

        /// <summary>
        /// Goalkeeper abilities use bits 2..64 (verified: the original goalkeepers only use these six bits). The order is the
        /// icon order of the game; it is an assumption until checked in the game.
        /// </summary>
        public static List<AbilityRule> DefaultGoalkeeperRules()
        {
            Func<AbilityRule, AbilityRule> gk = r => { r.Goalkeeper = true; return r; };
            return new List<AbilityRule>
            {
                gk(new AbilityRule("Elfmetertoeter",  2,  "avg", 16, 0, "reflexes", "one_on_ones", "anticipation")),
                gk(new AbilityRule("Reflexe",         4,  "avg", 16, 8, "reflexes")),
                gk(new AbilityRule("Herauslaufen",    8,  "avg", 15, 8, "rushing_out", "command_of_area")),
                gk(new AbilityRule("Fangsicherheit",  16, "avg", 16, 8, "handling")),
                gk(new AbilityRule("Fausten",         32, "avg", 15, 8, "punching", "aerial_reach")),
                gk(new AbilityRule("Ballsicherheit",  64, "avg", 15, 8, "kicking", "throwing"))
            };
        }

        /// <summary>2 goalkeepers, 1 libero, 4 central defenders, 2+2 backs, 2 defensive, 2+2 wide and 2 offensive (central) midfielders, 2 strikers = 21.</summary>
        public static int[] DefaultSquadQuota()
        {
            return new[] { 2, 1, 4, 2, 2, 2, 2, 2, 2, 2 };
        }

        /// <summary>1 goalkeeper, 2 central defenders, 1+1 backs, 1 defensive, 1 offensive (central), 1+1 wide midfielders, 2 strikers = 11.</summary>
        public static int[] DefaultAvereineQuota()
        {
            return new[] { 1, 0, 2, 1, 1, 1, 1, 1, 1, 2 };
        }

        public static ConversionSettings Load(string path)
        {
            using (var stream = File.OpenRead(path))
            {
                var serializer = new DataContractJsonSerializer(typeof(ConversionSettings));
                var loaded = (ConversionSettings)serializer.ReadObject(stream);
                // settings saved before goalkeeper rules existed get the defaults added
                if (loaded.AbilityRules != null && !loaded.AbilityRules.Any(r => r.Goalkeeper))
                    loaded.AbilityRules.AddRange(DefaultGoalkeeperRules());
                // settings saved before trait rules existed (the serializer does not run the constructor) get the defaults;
                // rules added in later versions are appended by name
                if (loaded.MaxTraits < 1) loaded.MaxTraits = 1;
                if (loaded.ClubInfo == null) loaded.ClubInfo = new List<ClubInfoOverride>();
                if (loaded.SeasonSlots == null) loaded.SeasonSlots = new List<SeasonSlotOverride>();
                if (loaded.PlayerLooks == null) loaded.PlayerLooks = new List<AppearanceOverride>();
                if (loaded.SquadQuota == null || loaded.SquadQuota.Length != 10) loaded.SquadQuota = DefaultSquadQuota();
                if (loaded.AvereineQuota == null || loaded.AvereineQuota.Length != 10) loaded.AvereineQuota = DefaultAvereineQuota();
                if (loaded.ExtraGoalkeeperCap < 1) loaded.ExtraGoalkeeperCap = 3;
                if (loaded.ExtraStrikerCap < 1) loaded.ExtraStrikerCap = 6;
                var defaults = TraitEngine.DefaultRules();
                if (loaded.TraitRules == null) loaded.TraitRules = defaults;
                else loaded.TraitRules.AddRange(defaults.Where(d => !loaded.TraitRules.Any(r => r.Name == d.Name)));
                return loaded;
            }
        }

        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var stream = File.Create(path))
            using (var writer = JsonReaderWriterFactory.CreateJsonWriter(stream, Encoding.UTF8, true, true, "  "))
            {
                new DataContractJsonSerializer(typeof(ConversionSettings)).WriteObject(writer, this);
            }
        }
    }
}
