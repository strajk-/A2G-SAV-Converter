using System;
using System.Collections.Generic;
using System.Linq;
using A2G.Convert.Fm;
using A2G.Convert.Plan;

namespace A2G.Convert.Engine
{
    public enum PlanProblemKind
    {
        /// <summary>A slot of the last season's results has no club (a league with fewer clubs than slots).</summary>
        EmptySlot,
        /// <summary>One club holds two places of the same kind (two European league places, or promoted twice).</summary>
        DuplicateSlot,
        /// <summary>Cup winner and cup finalist both already have a European place from the league: the second European cup gets no club of the country.</summary>
        CupPlaceLost,
        /// <summary>A competition has no title holder.</summary>
        NoTitleHolder
    }

    /// <summary>One thing in the plan's season results or European cup winners the game cannot work with.</summary>
    public sealed class PlanProblem
    {
        public PlanProblemKind Kind;
        public string Country, League;
        public string Slot, Slot2;
        public string Club;
        public int Competition;
    }

    /// <summary>
    /// Checks the last season's results (champion, runner-up, UEFA places, cup winner and finalist, promoted, relegated) and the winners of the European cups
    /// for what the game needs. A set of clubs that fails here can make the game abort while it simulates a European cup round (the draw of the second
    /// cup has an empty slot, see docs\Dev\conversion-rules.md); the converter therefore refuses to write such a data set.
    /// </summary>
    public static class PlanValidator
    {
        public static List<PlanProblem> Validate(ConversionPlan plan, ConversionSettings settings, string a2Folder)
        {
            var problems = new List<PlanProblem>();
            if (plan == null) return problems;
            foreach (var country in plan.Countries)
            {
                var tierNo = 0;
                foreach (var tier in country.Tiers)
                {
                    foreach (var league in tier.Leagues)
                    {
                        var clubs = league.Teams.Select(t => t.Club).Where(c => c != null).ToList();
                        if (clubs.Count == 0) continue;
                        var top = tierNo == 0;
                        var counts = string.IsNullOrEmpty(a2Folder) ? new LeagueCounts() : SeasonResults.CountsFor(a2Folder, country.Spec, league.Spec);
                        var slots = SeasonResults.Assign(settings, SeasonResults.LeagueKey(league.Spec), clubs, top, counts);
                        CheckLeague(problems, country.Spec.Key, league.Spec.Name, slots, top, counts);
                    }
                    tierNo++;
                }
            }
            foreach (var competition in TitleHolders.Competitions)
                if (TitleHolders.Effective(plan, settings, competition) == null)
                    problems.Add(new PlanProblem { Kind = PlanProblemKind.NoTitleHolder, Competition = competition });
            return problems;
        }

        private static void CheckLeague(List<PlanProblem> problems, string country, string league, Dictionary<string, FmClub> slots, bool top, LeagueCounts counts)
        {
            foreach (var slot in SeasonResults.Slots(top, counts))
                if (!slots.ContainsKey(slot))
                    problems.Add(new PlanProblem { Kind = PlanProblemKind.EmptySlot, Country = country, League = league, Slot = slot });

            // a club holds at most one place of the league kind (champion, runner-up, UEFA places / promoted / relegated)
            var byClub = slots.Where(kv => !SeasonResults.IsCupSlot(kv.Key)).GroupBy(kv => kv.Value);
            foreach (var group in byClub.Where(g => g.Count() > 1))
            {
                var keys = group.Select(kv => kv.Key).OrderBy(k => k, StringComparer.Ordinal).ToList();
                problems.Add(new PlanProblem { Kind = PlanProblemKind.DuplicateSlot, Country = country, League = league, Slot = keys[0], Slot2 = keys[1], Club = group.Key.Name });
            }
            if (!top) return;

            // the cup place of the second European cup: the winner of the cup, or the finalist when the winner has a league place already
            FmClub winner, finalist;
            if (slots.TryGetValue(SeasonResults.CupWinner, out winner) && slots.TryGetValue(SeasonResults.CupFinalist, out finalist))
            {
                var qualified = new HashSet<FmClub>(slots.Where(kv => kv.Key == SeasonResults.Champion || kv.Key == SeasonResults.RunnerUp
                                                                      || kv.Key.StartsWith("uefa", StringComparison.Ordinal)).Select(kv => kv.Value));
                if (qualified.Contains(winner) && qualified.Contains(finalist))
                    problems.Add(new PlanProblem { Kind = PlanProblemKind.CupPlaceLost, Country = country, League = league, Slot = SeasonResults.CupWinner, Slot2 = SeasonResults.CupFinalist });
            }
        }
    }
}
