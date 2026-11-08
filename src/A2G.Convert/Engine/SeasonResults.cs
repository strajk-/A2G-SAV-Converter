using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using A2G.Convert.Fm;
using A2G.Convert.Plan;
using A2G.Sav;

namespace A2G.Convert.Engine
{
    /// <summary>The league result of the last season as the game's editor knows it (legend of the league composition window).</summary>
    public enum LeagueResult { None = 0, Champion = 1, RunnerUp = 2, UefaPlace = 3, Promoted = 4, Relegated = 5 }

    public enum CupResult { None = 0, Winner = 1, Finalist = 2 }

    public struct SeasonResult
    {
        public LeagueResult League;
        public CupResult Cup;
    }

    /// <summary>How many clubs of a league get the places that are not unique (taken from the original data of that league).</summary>
    public sealed class LeagueCounts
    {
        public int Uefa, Promoted, Relegated;
    }

    /// <summary>
    /// The results of the last season the game keeps per team: champion, runner-up, UEFA places, cup winner and finalist, promoted and
    /// relegated clubs. In a Land*.sav they are the number at index 17 of the team's staff block; for the non-league clubs
    /// (Avereine) the champion and the cup winner of a nation are stored in the country triplets at the end of Avereine.sav.
    /// FM has no such data, so the defaults come from the last league position and the reputation (docs\Dev\conversion-rules.md).
    /// The user fills the slots (champion, runner-up, UEFA place 1, ...) with clubs of the league; a slot is identified by its name.
    /// </summary>
    public static class SeasonResults
    {
        public const int FlagIndex = 17;
        public const int CupWinnerBit = 64, CupFinalistBit = 128;
        /// <summary>The lowest three bits hold the league result (1 champion, 2 UEFA place, 3 relegated, 4 promoted, 6 runner-up).</summary>
        public const int LeagueMask = 7;

        public const string Champion = "champion", RunnerUp = "runnerup", CupWinner = "cupwinner", CupFinalist = "cupfinalist";
        public static string Uefa(int n) { return "uefa" + n; }
        public static string Promoted(int n) { return "promoted" + n; }
        public static string Relegated(int n) { return "relegated" + n; }

        /// <summary>The key of a playable league (its LIGA file) or of the non-league clubs of a nation ("AVE:" and its LAENDER index).</summary>
        public static string LeagueKey(A2LeagueSpec league) { return league.LigaFile; }
        public static string AvereineKey(int a2Country) { return "AVE:" + a2Country; }

        // ---- the values in the files --------------------------------------------------------

        public static int LeagueCode(LeagueResult r)
        {
            switch (r)
            {
                case LeagueResult.Champion: return 1;
                case LeagueResult.UefaPlace: return 2;
                case LeagueResult.Relegated: return 3;
                case LeagueResult.Promoted: return 4;
                case LeagueResult.RunnerUp: return 6;
                default: return 0;
            }
        }

        public static LeagueResult FromCode(int value)
        {
            switch (value & LeagueMask)
            {
                case 1: return LeagueResult.Champion;
                case 2: return LeagueResult.UefaPlace;
                case 3: return LeagueResult.Relegated;
                case 4: return LeagueResult.Promoted;
                case 6: return LeagueResult.RunnerUp;
                default: return LeagueResult.None;
            }
        }

        /// <summary>The staff block number: the template's other bits stay, the league result and the cup bits are replaced.</summary>
        public static int Encode(int templateValue, SeasonResult r)
        {
            var v = templateValue & ~(LeagueMask | CupWinnerBit | CupFinalistBit);
            v |= LeagueCode(r.League);
            if (r.Cup == CupResult.Winner) v |= CupWinnerBit;
            else if (r.Cup == CupResult.Finalist) v |= CupFinalistBit;
            return v;
        }

        // ---- the numbers of places ----------------------------------------------------------

        /// <summary>The numbers of places in a league, counted in the original team numbers of that league.</summary>
        public static LeagueCounts CountsFrom(IEnumerable<int> templateValues)
        {
            var c = new LeagueCounts();
            foreach (var v in templateValues)
                switch (FromCode(v))
                {
                    case LeagueResult.UefaPlace: c.Uefa++; break;
                    case LeagueResult.Promoted: c.Promoted++; break;
                    case LeagueResult.Relegated: c.Relegated++; break;
                }
            return c;
        }

        /// <summary>Counts of one league read from the original data files of an Anstoss 2 folder (zero counts if they can't be read).</summary>
        public static LeagueCounts CountsFor(string a2Folder, A2CountrySpec country, A2LeagueSpec league)
        {
            try
            {
                var def = Path.Combine(a2Folder, "DATA.DEF");
                var land = (LandFile)SavLoader.Load(Path.Combine(def, country.LandFile));
                var liga = (LigaFile)SavLoader.Load(Path.Combine(def, league.LigaFile));
                return CountsFrom(liga.TeamIds.Select(id => land.Teams[id - 1].StaffBlock.Values[FlagIndex]));
            }
            catch (Exception) { return new LeagueCounts(); }
        }

        // ---- slots --------------------------------------------------------------------------

        /// <summary>The slots of a league in the order the window shows them: the top division has champion, runner-up, UEFA places, cup winner, cup finalist and promoted; a lower one relegated and promoted.</summary>
        public static List<string> Slots(bool topDivision, LeagueCounts counts)
        {
            var slots = new List<string>();
            if (topDivision)
            {
                slots.Add(Champion); slots.Add(RunnerUp);
                for (var i = 1; i <= counts.Uefa; i++) slots.Add(Uefa(i));
                slots.Add(CupWinner); slots.Add(CupFinalist);
            }
            else
                for (var i = 1; i <= counts.Relegated; i++) slots.Add(Relegated(i));
            for (var i = 1; i <= counts.Promoted; i++) slots.Add(Promoted(i));
            return slots;
        }

        public static bool IsCupSlot(string slot) { return slot == CupWinner || slot == CupFinalist; }

        /// <summary>Last position first (0 = unknown goes last), then the stronger club.</summary>
        private static IOrderedEnumerable<FmClub> Ranked(IEnumerable<FmClub> clubs)
        {
            return clubs.OrderBy(c => c.LastLeaguePosition > 0 ? c.LastLeaguePosition : int.MaxValue)
                        .ThenByDescending(c => c.Reputation).ThenBy(c => c.Name, StringComparer.Ordinal);
        }

        /// <summary>
        /// The rule-based occupants: the weakest clubs by reputation are the promoted ones, in a lower division the strongest are the
        /// relegated ones; in the top division the others are ranked by last position: champion, runner-up, then the UEFA places.
        /// The cup winner is the champion and the finalist the runner-up (FM has no cup results).
        /// </summary>
        public static Dictionary<string, FmClub> AutoSlots(IList<FmClub> clubs, bool topDivision, LeagueCounts counts)
        {
            var result = new Dictionary<string, FmClub>();
            if (clubs.Count == 0) return result;
            var third = Math.Max(0, clubs.Count / 3);
            var byReputation = clubs.OrderByDescending(c => c.Reputation).ThenBy(c => c.Name, StringComparer.Ordinal).ToList();
            var used = new HashSet<FmClub>();
            var promoted = byReputation.AsEnumerable().Reverse().Take(Math.Min(counts.Promoted, third)).ToList();
            for (var i = 0; i < promoted.Count; i++) { result[Promoted(i + 1)] = promoted[i]; used.Add(promoted[i]); }
            if (!topDivision)
            {
                var relegated = byReputation.Where(c => !used.Contains(c)).Take(Math.Min(counts.Relegated, third)).ToList();
                for (var i = 0; i < relegated.Count; i++) result[Relegated(i + 1)] = relegated[i];
                return result;
            }
            var ranked = Ranked(clubs.Where(c => !used.Contains(c))).ToList();
            if (ranked.Count > 0) { result[Champion] = ranked[0]; result[CupWinner] = ranked[0]; }
            if (ranked.Count > 1) { result[RunnerUp] = ranked[1]; result[CupFinalist] = ranked[1]; }
            for (var i = 0; i < counts.Uefa && 2 + i < ranked.Count; i++) result[Uefa(i + 1)] = ranked[2 + i];
            return result;
        }

        /// <summary>The slots with the user's choices on top. A club picked for a slot leaves the slot it held before (the two swap); a cup slot is separate from the league slots.</summary>
        public static Dictionary<string, FmClub> Assign(ConversionSettings settings, string groupKey, IList<FmClub> clubs, bool topDivision, LeagueCounts counts)
        {
            var slots = AutoSlots(clubs, topDivision, counts);
            var valid = Slots(topDivision, counts);
            if (settings == null || settings.SeasonSlots == null) return slots;
            foreach (var o in settings.SeasonSlots.Where(x => x.Group == groupKey && valid.Contains(x.Slot)))
            {
                var club = clubs.FirstOrDefault(c => c.Uid == o.Uid);
                if (club == null) continue;
                FmClub previous;
                slots.TryGetValue(o.Slot, out previous);
                var family = IsCupSlot(o.Slot);
                var holds = slots.Where(kv => kv.Key != o.Slot && kv.Value == club && IsCupSlot(kv.Key) == family).Select(kv => kv.Key).ToList();
                foreach (var key in holds)
                {
                    if (previous != null) slots[key] = previous; else slots.Remove(key);
                }
                slots[o.Slot] = club;
            }
            return slots;
        }

        /// <summary>The values for the clubs of the league: what the slots they hold stand for.</summary>
        public static Dictionary<FmClub, SeasonResult> Results(Dictionary<string, FmClub> slots)
        {
            var result = new Dictionary<FmClub, SeasonResult>();
            Func<FmClub, SeasonResult> get = c => { SeasonResult r; result.TryGetValue(c, out r); return r; };
            foreach (var kv in slots)
            {
                var r = get(kv.Value);
                if (kv.Key == Champion) r.League = LeagueResult.Champion;
                else if (kv.Key == RunnerUp) r.League = LeagueResult.RunnerUp;
                else if (kv.Key.StartsWith("uefa", StringComparison.Ordinal)) r.League = LeagueResult.UefaPlace;
                else if (kv.Key.StartsWith("promoted", StringComparison.Ordinal)) r.League = LeagueResult.Promoted;
                else if (kv.Key.StartsWith("relegated", StringComparison.Ordinal)) r.League = LeagueResult.Relegated;
                else if (kv.Key == CupWinner) r.Cup = CupResult.Winner;
                else if (kv.Key == CupFinalist) r.Cup = CupResult.Finalist;
                result[kv.Value] = r;
            }
            return result;
        }

        public static Dictionary<FmClub, SeasonResult> Effective(ConversionSettings settings, string groupKey, IList<FmClub> clubs, bool topDivision, LeagueCounts counts)
        {
            return Results(Assign(settings, groupKey, clubs, topDivision, counts));
        }

        // ---- non-league clubs: champion and cup winner of a nation ---------------------------

        public static List<string> AvereineSlots { get { return new List<string> { Champion, CupWinner }; } }

        /// <summary>The champion (best last position) and the cup winner (the second best) of a nation, with the user's choices on top.</summary>
        public static Dictionary<string, FmClub> AssignAvereine(ConversionSettings settings, string groupKey, IList<FmClub> clubs)
        {
            var slots = new Dictionary<string, FmClub>();
            if (clubs.Count == 0) return slots;
            var ranked = Ranked(clubs).ToList();
            slots[Champion] = ranked[0];
            slots[CupWinner] = ranked.Count > 1 ? ranked[1] : ranked[0];
            if (settings != null && settings.SeasonSlots != null)
                foreach (var o in settings.SeasonSlots.Where(x => x.Group == groupKey && (x.Slot == Champion || x.Slot == CupWinner)))
                {
                    var club = clubs.FirstOrDefault(c => c.Uid == o.Uid);
                    if (club != null) slots[o.Slot] = club;
                }
            return slots;
        }

        // ---- the user's choices -------------------------------------------------------------

        public static bool IsOverridden(ConversionSettings settings, string groupKey, string slot)
        {
            return settings.SeasonSlots != null && settings.SeasonSlots.Any(x => x.Group == groupKey && x.Slot == slot);
        }

        /// <summary>Puts a club into a slot (null = back to the rule).</summary>
        public static void SetSlot(ConversionSettings settings, string groupKey, string slot, FmClub club)
        {
            settings.SeasonSlots.RemoveAll(x => x.Group == groupKey && x.Slot == slot);
            if (club != null) settings.SeasonSlots.Add(new SeasonSlotOverride { Group = groupKey, Slot = slot, Uid = club.Uid });
        }

        /// <summary>Forgets all choices of a league or nation.</summary>
        public static void ResetGroup(ConversionSettings settings, string groupKey)
        {
            settings.SeasonSlots.RemoveAll(x => x.Group == groupKey);
        }
    }
}
