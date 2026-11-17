using System;
using System.Collections.Generic;
using System.Linq;
using A2G.Convert.Fm;
using A2G.Convert.Plan;

namespace A2G.Convert.Engine
{
    /// <summary>
    /// The winners of last season's European cups, which the editor lists as "Vorjahressieger Europapokale" (KLEINIG.SAV): the title holder of
    /// a competition has a place in it again. FM saves have no trophy data, so the user chooses the clubs; without a choice the rules below
    /// pick clubs of the five main nations. A title holder has to be a team of the main nations' Land files (country number and team number).
    /// </summary>
    public static class TitleHolders
    {
        public const int ChampionsLeague = 0, CupWinners = 1, UefaCup = 2;
        public static readonly int[] Competitions = { ChampionsLeague, CupWinners, UefaCup };

        /// <summary>The choice stored in the settings (the FM club id), or null.</summary>
        public static string Chosen(ConversionSettings settings, int competition)
        {
            if (settings == null) return null;
            switch (competition)
            {
                case ChampionsLeague: return settings.TitleChampionsLeague;
                case CupWinners: return settings.TitleCupWinners;
                default: return settings.TitleUefaCup;
            }
        }

        public static void Choose(ConversionSettings settings, int competition, string clubUid)
        {
            if (string.IsNullOrEmpty(clubUid)) clubUid = null;
            switch (competition)
            {
                case ChampionsLeague: settings.TitleChampionsLeague = clubUid; break;
                case CupWinners: settings.TitleCupWinners = clubUid; break;
                default: settings.TitleUefaCup = clubUid; break;
            }
        }

        /// <summary>Every club that can be a title holder: the teams of the main nations' Land files.</summary>
        public static List<FmClub> Candidates(ConversionPlan plan)
        {
            return plan == null ? new List<FmClub>() : plan.Countries.SelectMany(c => c.AllTeams).Where(t => t.Club != null).Select(t => t.Club).Distinct().ToList();
        }

        /// <summary>
        /// The rule used without a choice: the clubs of the five main nations' top divisions by reputation; the Champions League holder is the
        /// strongest, the UEFA Cup holder the fifth, the Cup Winners' Cup holder the tenth (a spread of clubs rather than one club three times).
        /// </summary>
        public static FmClub Default(ConversionPlan plan, int competition)
        {
            if (plan == null) return null;
            var top = plan.Countries.SelectMany(c => c.Tiers.Take(1)).SelectMany(t => t.Leagues).SelectMany(l => l.Teams).Where(t => t.Club != null)
                .Select(t => t.Club).OrderByDescending(c => c.Reputation).ThenBy(c => c.Name, StringComparer.Ordinal).ToList();
            if (top.Count == 0) return null;
            var index = competition == ChampionsLeague ? 0 : competition == UefaCup ? 4 : 9;
            return top[Math.Min(index, top.Count - 1)];
        }

        /// <summary>The title holder that is written: the user's choice when that club is still a team of the plan, else the rule's club.</summary>
        public static FmClub Effective(ConversionPlan plan, ConversionSettings settings, int competition)
        {
            var uid = Chosen(settings, competition);
            if (uid != null)
            {
                var club = Candidates(plan).FirstOrDefault(c => c.Uid == uid);
                if (club != null) return club;
            }
            return Default(plan, competition);
        }
    }
}
