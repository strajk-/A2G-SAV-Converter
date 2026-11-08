using System;
using System.Collections.Generic;
using System.Linq;
using A2G.Convert.Fm;
using A2G.Convert.Plan;

namespace A2G.Convert.Engine
{
    /// <summary>
    /// Turns FM data into a <see cref="ConversionPlan"/>. League sizes are fixed in Anstoss 2, so the clubs of each
    /// country are distributed by a top-down reputation cascade (see docs\Dev\conversion-rules.md):
    /// the A2 slot groups are the tiers in order, then the spare clubs; each group takes the highest-reputation clubs
    /// of its FM tier plus the clubs pushed down from above, pushes its surplus down and, if it lacks clubs, pulls the
    /// highest-reputation clubs up from below.
    /// </summary>
    public static class PlanBuilder
    {
        public static ConversionPlan Build(FmDataSet fm, ConversionSettings settings)
        {
            var plan = new ConversionPlan();
            var mainNationIds = new HashSet<int>();

            foreach (var spec in A2Structure.Countries())
            {
                var country = BuildCountry(spec, fm, settings, plan.Warnings);
                plan.Countries.Add(country);
                var nation = fm.NationByName(spec.FmNationName);
                if (nation != null) mainNationIds.Add(nation.SaveId);
            }

            BuildAvereine(plan, fm, settings, mainNationIds);
            return plan;
        }

        // ---- the five main countries --------------------------------------------------------

        public static CountryPlan BuildCountry(A2CountrySpec spec, FmDataSet fm, ConversionSettings s, List<string> warnings)
        {
            var country = new CountryPlan(spec);
            var nation = fm.NationByName(spec.FmNationName);
            if (nation == null)
            {
                warnings.Add(spec.Key + ": nation '" + spec.FmNationName + "' not found in the FM data (is nation_names.csv present?).");
                return country;
            }

            var candidates = fm.Clubs.Values.Where(c => c.NationId == nation.SaveId && c.IsMainMensClub).ToList();
            var squadClubs = candidates.Where(c => SquadSelector.Qualifies(c, fm, s)).ToList();
            var squadSet = new HashSet<FmClub>(squadClubs);

            // pools: one per detected FM league (tier order), then everybody else with a squad
            var pools = new List<List<FmClub>>();
            var used = new HashSet<FmClub>();
            foreach (var league in fm.Leagues.Where(l => l.NationId == nation.SaveId).OrderBy(l => l.Tier))
                pools.Add(league.Clubs.Where(c => squadSet.Contains(c) && used.Add(c)).ToList());
            if (pools.Count == 0)
                warnings.Add(spec.Key + ": no FM leagues detected (no fixtures.csv?); clubs are ordered by reputation only.");
            pools.Add(squadClubs.Where(c => !used.Contains(c)).ToList());

            var groupSizes = spec.Tiers.Select(t => t.Size).Concat(new[] { spec.SpareTeams }).ToList();
            while (pools.Count < groupSizes.Count + 1) pools.Add(new List<FmClub>());

            var groups = new List<List<FmClub>>();
            for (var t = 0; t < groupSizes.Count; t++)
            {
                var size = groupSizes[t];
                var current = ByReputation(pools[t]);
                var take = current.Take(size).ToList();
                pools[t + 1].AddRange(current.Skip(size));   // surplus is inserted a tier lower
                pools[t] = new List<FmClub>();
                while (take.Count < size)                    // gap: the best clubs of the tiers below move up
                {
                    var best = Enumerable.Range(t + 1, pools.Count - t - 1)
                        .SelectMany(i => pools[i].Select(c => new { Club = c, Pool = i }))
                        .OrderByDescending(x => x.Club.Reputation).ThenBy(x => x.Club.Name, StringComparer.Ordinal)
                        .FirstOrDefault();
                    if (best == null) break;
                    pools[best.Pool].Remove(best.Club);
                    take.Add(best.Club);
                }
                if (take.Count < size)
                    warnings.Add(string.Format("{0}: slot group {1} has only {2} of {3} clubs with squads.",
                        spec.Key, t + 1, take.Count, size));
                groups.Add(ByReputation(take));
            }

            // distribute into leagues
            for (var t = 0; t < spec.Tiers.Count; t++)
            {
                var tier = country.Tiers[t];
                var clubs = groups[t];
                var leagueCount = tier.Leagues.Count;
                for (var i = 0; i < clubs.Count; i++)
                {
                    var round = i / leagueCount;
                    var pos = i % leagueCount;
                    var league = tier.Leagues[round % 2 == 0 ? pos : leagueCount - 1 - pos];   // snake order
                    league.Teams.Add(new TeamPlan(clubs[i]));
                }
            }
            foreach (var club in groups[groups.Count - 1]) country.Spares.Add(new TeamPlan(club));

            // tail names: the next best clubs, squad or not
            var chosen = new HashSet<FmClub>(groups.SelectMany(g => g));
            var leftovers = candidates.Where(c => !chosen.Contains(c));
            country.TailClubs.AddRange(ByReputation(leftovers.ToList()).Take(spec.TailClubNames));
            if (country.TailClubs.Count < spec.TailClubNames)
                warnings.Add(string.Format("{0}: only {1} of {2} spare club names available.",
                    spec.Key, country.TailClubs.Count, spec.TailClubNames));

            foreach (var team in country.AllTeams)
                team.Squad.AddRange(SquadSelector.SelectBalanced(team.Club, fm, s));
            return country;
        }

        // ---- non-league clubs (Avereine.sav) ------------------------------------------------

        private static void BuildAvereine(ConversionPlan plan, FmDataSet fm, ConversionSettings s, HashSet<int> mainNationIds)
        {
            var eligible = fm.Clubs.Values
                .Where(c => c.IsMainMensClub && !mainNationIds.Contains(c.NationId))
                .ToList();

            // Clubs with a real squad first (reputation order), then clubs with only a few players and finally clubs without any:
            // missing players are added from the unused players (SquadSelector.TopUpAvereine).
            var real = eligible.ToDictionary(c => c, c => fm.PlayersOf(c).Count(SquadSelector.Eligible));
            var withSquad = ByReputation(eligible.Where(c => real[c] >= 6).ToList())
                .Concat(ByReputation(eligible.Where(c => real[c] >= 1 && real[c] < 6).ToList()))
                .Concat(ByReputation(eligible.Where(c => real[c] == 0).ToList())).ToList();
            // the game expects the shipped nation layout: 30 European nations with 3-6 clubs each, sorted by nation
            var picked = AvereineSlots.Pick(withSquad, AvereineSlots.Clubs, fm, plan.Warnings, "Avereine");
            var wanted = AvereineSlots.Clubs.Sum(x => x.Count);
            if (picked.Count < wanted)
                plan.Warnings.Add(string.Format("Avereine: only {0} of {1} clubs with squads available in the FM data; the other slots keep the original club of the same nation (the game needs its fixed nation layout).", picked.Count, wanted));

            foreach (var club in picked)
            {
                var team = new TeamPlan(club);
                team.Squad.AddRange(SquadSelector.SelectAvereine(club, fm, s));
                plan.Avereine.Teams.Add(team);
            }

            // thin squads are completed from the players that are in no team of the plan: the club's nation first, the Current Ability
            // closest to the club's own average (a club without players uses its nation's average, else the average of all clubs)
            var pool = new SquadSelector.AvereinePool(fm, plan.Countries.SelectMany(c => c.AllTeams).Concat(plan.Avereine.Teams).SelectMany(t => t.Squad));
            var nationAverage = plan.Avereine.Teams.Where(t => t.Squad.Count > 0).GroupBy(t => t.Club.NationId)
                .ToDictionary(g => g.Key, g => g.SelectMany(t => t.Squad).Average(p => (double)p.CurrentAbility));
            var allPlayers = plan.Avereine.Teams.SelectMany(t => t.Squad).ToList();
            var overall = allPlayers.Count > 0 ? allPlayers.Average(p => (double)p.CurrentAbility) : 80.0;
            var details = new List<string>();
            foreach (var team in plan.Avereine.Teams)
            {
                double target;
                var original = team.Squad.ToList();
                var before = original.Count;
                if (before > 0) target = team.Squad.Average(p => (double)p.CurrentAbility);
                else if (!nationAverage.TryGetValue(team.Club.NationId, out target)) target = overall;
                SquadSelector.TopUpAvereine(team.Squad, team.Club, target, pool, s);
                var added = team.Squad.Where(p => !original.Contains(p)).ToList();
                if (added.Count == 0) continue;
                var nation = fm.NationBySaveId(team.Club.NationId);
                details.Add(string.Format("    {0} ({1}): {2} real players in the FM data, {3} added from unused players ({4} of the same nation, target CA {5:0})",
                    team.Club.Name, nation != null ? nation.Name : "?", before, added.Count, added.Count(p => p.NationId == team.Club.NationId), target));
            }
            if (details.Count > 0)
            {
                plan.Warnings.Add("Avereine: " + details.Count + " non-league clubs have fewer than " + s.AvereineSquadSize +
                    " players in the FM data (insufficient FM data for their nation) and were completed from unused players, same nation first, similar Current Ability:");
                plan.Warnings.AddRange(details);
            }

            var pickedSet = new HashSet<FmClub>(picked);
            var rest = ByReputation(eligible.Where(c => !pickedSet.Contains(c)).ToList());
            // the 40 spare club names are overseas clubs of 32 fixed nations, also sorted by nation
            var extras = AvereineSlots.Pick(rest, AvereineSlots.Extras, fm, plan.Warnings, "Avereine spare clubs");
            plan.Avereine.ExtraClubs.AddRange(extras);
        }

        private static List<FmClub> PickWithNationCap(List<FmClub> ordered, int count, int cap)
        {
            var result = new List<FmClub>();
            var perNation = new Dictionary<int, int>();
            foreach (var club in ordered)
            {
                if (result.Count >= count) break;
                int n;
                perNation.TryGetValue(club.NationId, out n);
                if (n >= cap) continue;
                perNation[club.NationId] = n + 1;
                result.Add(club);
            }
            return result;
        }

        private static List<FmClub> ByReputation(IEnumerable<FmClub> clubs)
        {
            return clubs.OrderByDescending(c => c.Reputation).ThenBy(c => c.Name, StringComparer.Ordinal).ToList();
        }
    }

    /// <summary>Chooses the players of a team.</summary>
    public static class SquadSelector
    {
        /// <summary>Anstoss 2 players are at least 17 years old: younger FM players are never picked.</summary>
        public const int MinAge = 17;

        public static bool Eligible(FmPlayer p) { return p.Age >= MinAge; }

        /// <summary>
        /// A club can fill a league slot when it has enough real eligible players (<see cref="ConversionSettings.MinPlayersForTeam"/>)
        /// and a goalkeeper; missing players up to the team minimum are added from the free pool (an FM save only loads some
        /// leagues completely, so lower-league clubs often have small squads).
        /// </summary>
        public static bool Qualifies(FmClub club, FmDataSet fm, ConversionSettings s)
        {
            var players = fm.PlayersOf(club).Where(Eligible).ToList();
            return players.Count >= s.MinPlayersForTeam && players.Any(p => p.IsGoalkeeper);
        }

        // ---- balanced selection for the playable leagues ----------------------------------------

        /// <summary>
        /// When a position has too few players, replacements come from these positions, in this order (slot k starts at the
        /// k-th entry so several missing players come from different positions: no striker -> offensive midfielder, right
        /// midfielder, left midfielder ...). A missing libero is replaced by an offensive midfielder.
        /// </summary>
        private static readonly Dictionary<int, int[]> Chains = new Dictionary<int, int[]>
        {
            { PositionRules.Libero, new[] { PositionRules.OffensiveMidfield, PositionRules.CentralDefender } },
            { PositionRules.CentralDefender, new[] { PositionRules.Libero, PositionRules.DefensiveMidfield, PositionRules.LeftBack, PositionRules.RightBack } },
            { PositionRules.LeftBack, new[] { PositionRules.LeftMidfield, PositionRules.RightBack, PositionRules.CentralDefender } },
            { PositionRules.RightBack, new[] { PositionRules.RightMidfield, PositionRules.LeftBack, PositionRules.CentralDefender } },
            { PositionRules.DefensiveMidfield, new[] { PositionRules.OffensiveMidfield, PositionRules.CentralDefender } },
            { PositionRules.OffensiveMidfield, new[] { PositionRules.DefensiveMidfield, PositionRules.LeftMidfield, PositionRules.RightMidfield } },
            { PositionRules.LeftMidfield, new[] { PositionRules.OffensiveMidfield, PositionRules.RightMidfield, PositionRules.LeftBack } },
            { PositionRules.RightMidfield, new[] { PositionRules.OffensiveMidfield, PositionRules.LeftMidfield, PositionRules.RightBack } },
            { PositionRules.Striker, new[] { PositionRules.OffensiveMidfield, PositionRules.RightMidfield, PositionRules.LeftMidfield } },
            { PositionRules.Goalkeeper, new int[0] }
        };

        /// <summary>The fewest players kept per position when a squad has to be trimmed below the quota total.</summary>
        private static readonly Dictionary<int, int> HardMinimum = new Dictionary<int, int>
        {
            { 1, 2 }, { 2, 0 }, { 3, 2 }, { 4, 1 }, { 5, 1 }, { 6, 1 }, { 7, 1 }, { 8, 1 }, { 9, 1 }, { 10, 1 }
        };

        private sealed class Info
        {
            public FmPlayer Player;
            public int Primary;
            public List<int> Secondary;
            /// <summary>Pick score (current ability plus the counted share of the growth room).</summary>
            public double Score;
        }

        /// <summary>
        /// Squad of a league team: the quota per position (<see cref="ConversionSettings.SquadQuota"/>), best Current Ability first,
        /// first team before reserves. Shortfalls are filled along <see cref="Chains"/>, then with any remaining player of the club, then with
        /// then free players of the nation if the squad stays below the team minimum. When <see cref="ConversionSettings.SquadSize"/> is larger than the quotas the best remaining players are added (goalkeepers
        /// and strikers are capped), when it is smaller the weakest players of crowded positions are dropped.
        /// </summary>
        public static List<FmPlayer> SelectBalanced(FmClub club, FmDataSet fm, ConversionSettings s)
        {
            return Build(club, fm, s, s.SquadQuota, s.SquadSize, true);
        }

        /// <summary>Squad of a non-league (Avereine) club: its own quota (11 players), no free pool.</summary>
        public static List<FmPlayer> SelectAvereine(FmClub club, FmDataSet fm, ConversionSettings s)
        {
            return Build(club, fm, s, s.AvereineQuota, s.AvereineSquadSize, false);
        }

        private static List<FmPlayer> Build(FmClub club, FmDataSet fm, ConversionSettings s, int[] quotaSource, int size, bool freePool)
        {
            // best pick score first (so a young talent can outrank an established player); first team breaks ties
            var infos = fm.PlayersOf(club).Where(Eligible).Select(p =>
            {
                var scores = PositionRules.Scores(p);
                var primary = PositionRules.Primary(scores);
                return new Info { Player = p, Primary = primary, Secondary = PositionRules.Secondary(scores, primary, s.SecondaryPositionMinRating), Score = s.PickScore(p) };
            }).OrderByDescending(i => i.Score)
              .ThenByDescending(i => i.Player.TeamId == club.FirstTeamId ? 1 : 0)
              .ThenByDescending(i => i.Player.CurrentAbility)
              .ThenBy(i => i.Player.Uid, StringComparer.Ordinal).ToList();

            var quota = (quotaSource != null && quotaSource.Length == 10 ? quotaSource : ConversionSettings.DefaultSquadQuota()).ToArray();
            // a club without a libero gives that quota to an offensive midfielder
            if (quota[PositionRules.Libero - 1] > 0 && !infos.Any(i => i.Primary == PositionRules.Libero || i.Secondary.Contains(PositionRules.Libero)))
            {
                quota[PositionRules.OffensiveMidfield - 1] += quota[PositionRules.Libero - 1];
                quota[PositionRules.Libero - 1] = 0;
            }

            var chosen = new List<Info>();
            var order = new[] { 1, 3, 4, 5, 2, 6, 9, 7, 8, 10 };
            var missing = new Dictionary<int, int>();
            var unfilled = new List<int>();   // positions the club itself could not fill
            foreach (var pos in order)
            {
                var take = infos.Where(i => i.Primary == pos).Take(quota[pos - 1]).ToList();
                chosen.AddRange(take);
                missing[pos] = quota[pos - 1] - take.Count;
            }


            foreach (var pos in order)
            {
                for (var k = 0; k < missing[pos]; k++)
                {
                    var pick = Replacement(pos, k, infos, chosen);
                    if (pick != null) chosen.Add(pick); else unfilled.Add(pos);
                }
            }

            // a larger squad: the best remaining players, goalkeepers and strikers capped
            if (chosen.Count < size)
            {
                foreach (var i in infos.Where(i => !chosen.Contains(i)))
                {
                    if (chosen.Count >= size) break;
                    if (i.Primary == PositionRules.Goalkeeper && chosen.Count(c => c.Primary == PositionRules.Goalkeeper) >= s.ExtraGoalkeeperCap) continue;
                    if (i.Primary == PositionRules.Striker && chosen.Count(c => c.Primary == PositionRules.Striker) >= s.ExtraStrikerCap) continue;
                    chosen.Add(i);
                }
            }
            // a smaller squad: drop the weakest player of a position that has more than its minimum
            while (chosen.Count > size)
            {
                var drop = chosen.Where(c => chosen.Count(x => x.Primary == c.Primary) > HardMinimum[c.Primary])
                    .OrderBy(c => c.Score).FirstOrDefault();
                if (drop == null) break;
                chosen.Remove(drop);
            }

            var result = chosen.Select(c => c.Player).ToList();
            if (freePool) FillFromFreePool(result, unfilled, club, fm, s);
            return result.OrderByDescending(p => p.CurrentAbility).ToList();
        }

        /// <summary>
        /// Last resort for data gaps (not a feature: Anstoss 2 has no free agents, the players simply join the team): a team
        /// needs <see cref="ConversionSettings.MinGoalkeepers"/> goalkeepers and <see cref="ConversionSettings.TeamMinimum"/> players.
        /// Free FM players of the club's nation fill the open positions first, then the best remaining ones.
        /// </summary>
        private static void FillFromFreePool(List<FmPlayer> squad, List<int> unfilled, FmClub club, FmDataSet fm, ConversionSettings s)
        {
            Func<FmPlayer, int> primary = p => PositionRules.Primary(PositionRules.Scores(p));
            var needKeepers = s.MinGoalkeepers - squad.Count(p => primary(p) == PositionRules.Goalkeeper);
            if (needKeepers <= 0 && squad.Count >= s.TeamMinimum) return;
            var free = fm.FreeAgentsOf(club.NationId).Where(Eligible).OrderByDescending(p => s.PickScore(p)).ToList();
            if (needKeepers > 0)
            {
                var keepers = free.Where(p => primary(p) == PositionRules.Goalkeeper)
                    .Concat(fm.FreeGoalkeepers().Where(Eligible)).Distinct().Where(p => !squad.Contains(p)).Take(needKeepers).ToList();
                squad.AddRange(keepers);
            }
            foreach (var pos in unfilled)
            {
                if (squad.Count >= s.TeamMinimum) break;
                var pick = free.FirstOrDefault(p => !squad.Contains(p) && primary(p) == pos);
                if (pick != null) squad.Add(pick);
            }
            foreach (var p in free)
            {
                if (squad.Count >= s.TeamMinimum) break;
                if (!squad.Contains(p) && primary(p) != PositionRules.Goalkeeper) squad.Add(p);
            }
        }

        // ---- Avereine top-up from the unused players ----------------------------------------------

        /// <summary>
        /// The FM players that are in no team of the plan, by nation and position. Small nations have few loaded players, so a
        /// non-league club with a thin squad is completed from here: players of the club's nation first, the Current Ability closest
        /// to the club's own average first.
        /// </summary>
        public sealed class AvereinePool
        {
            private readonly Dictionary<int, Dictionary<int, List<FmPlayer>>> _byNation = new Dictionary<int, Dictionary<int, List<FmPlayer>>>();
            private readonly Dictionary<int, List<FmPlayer>> _byPosition = new Dictionary<int, List<FmPlayer>>();
            private readonly HashSet<FmPlayer> _taken = new HashSet<FmPlayer>();

            public AvereinePool(FmDataSet fm, IEnumerable<FmPlayer> usedPlayers)
            {
                foreach (var p in usedPlayers) _taken.Add(p);
                foreach (var p in fm.Players)
                {
                    if (!Eligible(p) || _taken.Contains(p)) continue;
                    var pos = PositionRules.Primary(PositionRules.Scores(p));
                    Dictionary<int, List<FmPlayer>> nation;
                    if (!_byNation.TryGetValue(p.NationId, out nation)) _byNation[p.NationId] = nation = new Dictionary<int, List<FmPlayer>>();
                    Add(nation, pos, p);
                    Add(_byPosition, pos, p);
                }
            }

            private static void Add(Dictionary<int, List<FmPlayer>> map, int pos, FmPlayer p)
            {
                List<FmPlayer> list;
                if (!map.TryGetValue(pos, out list)) map[pos] = list = new List<FmPlayer>();
                list.Add(p);
            }

            public void MarkUsed(FmPlayer p) { _taken.Add(p); }

            /// <summary>The unused player of the position closest to the target ability: the club's nation first, then any nation.</summary>
            public FmPlayer Find(int nationId, int position, double targetCa)
            {
                Dictionary<int, List<FmPlayer>> nation;
                List<FmPlayer> list;
                if (_byNation.TryGetValue(nationId, out nation) && nation.TryGetValue(position, out list))
                {
                    var p = Closest(list, targetCa);
                    if (p != null) return p;
                }
                return _byPosition.TryGetValue(position, out list) ? Closest(list, targetCa) : null;
            }

            /// <summary>Any unused outfield player: the club's nation first.</summary>
            public FmPlayer FindAnyOutfield(int nationId, double targetCa)
            {
                Dictionary<int, List<FmPlayer>> nation;
                if (_byNation.TryGetValue(nationId, out nation))
                {
                    var p = Closest(nation.Where(kv => kv.Key != PositionRules.Goalkeeper).SelectMany(kv => kv.Value), targetCa);
                    if (p != null) return p;
                }
                return Closest(_byPosition.Where(kv => kv.Key != PositionRules.Goalkeeper).SelectMany(kv => kv.Value), targetCa);
            }

            private FmPlayer Closest(IEnumerable<FmPlayer> list, double targetCa)
            {
                FmPlayer best = null;
                var bestDiff = double.MaxValue;
                foreach (var p in list)
                {
                    if (_taken.Contains(p)) continue;
                    var diff = Math.Abs(p.CurrentAbility - targetCa);
                    if (diff < bestDiff) { bestDiff = diff; best = p; }
                }
                return best;
            }
        }

        /// <summary>
        /// Completes a thin non-league squad up to <see cref="ConversionSettings.AvereineSquadSize"/> players following the quota per
        /// position: goalkeeper first, then the open positions; players come from <paramref name="pool"/>.
        /// </summary>
        public static void TopUpAvereine(List<FmPlayer> squad, FmClub club, double targetCa, AvereinePool pool, ConversionSettings s)
        {
            var size = s.AvereineSquadSize;
            var quota = (s.AvereineQuota != null && s.AvereineQuota.Length == 10 ? s.AvereineQuota : ConversionSettings.DefaultAvereineQuota()).ToArray();
            quota[PositionRules.OffensiveMidfield - 1] += quota[PositionRules.Libero - 1];   // the pool picks no liberos
            quota[PositionRules.Libero - 1] = 0;
            var counts = new int[11];
            foreach (var p in squad) counts[PositionRules.Primary(PositionRules.Scores(p))]++;
            // a club without a goalkeeper gets one even when its squad is otherwise full: the weakest outfield player makes room
            while (counts[PositionRules.Goalkeeper] < Math.Max(1, quota[PositionRules.Goalkeeper - 1]))
            {
                var keeper = pool.Find(club.NationId, PositionRules.Goalkeeper, targetCa);
                if (keeper == null) break;
                if (squad.Count >= size)
                {
                    var weakest = squad.Where(p => PositionRules.Primary(PositionRules.Scores(p)) != PositionRules.Goalkeeper)
                        .OrderByDescending(p => counts[PositionRules.Primary(PositionRules.Scores(p))]).ThenBy(p => p.CurrentAbility).FirstOrDefault();
                    if (weakest == null) break;
                    squad.Remove(weakest);
                    counts[PositionRules.Primary(PositionRules.Scores(weakest))]--;
                }
                squad.Add(keeper); pool.MarkUsed(keeper); counts[PositionRules.Goalkeeper]++;
            }
            if (squad.Count >= size) return;
            foreach (var pos in new[] { 1, 3, 4, 5, 6, 9, 7, 8, 10 })
                while (squad.Count < size && counts[pos] < quota[pos - 1])
                {
                    var pick = pool.Find(club.NationId, pos, targetCa);
                    if (pick == null) break;
                    squad.Add(pick); pool.MarkUsed(pick); counts[pos]++;
                }
            while (squad.Count < size)
            {
                var pick = pool.FindAnyOutfield(club.NationId, targetCa);
                if (pick == null) break;
                squad.Add(pick); pool.MarkUsed(pick);
            }
        }

        /// <summary>Finds a replacement for a missing player of position <paramref name="pos"/> (the k-th missing one).</summary>
        private static Info Replacement(int pos, int k, List<Info> club, List<Info> chosen)
        {
            Func<Func<Info, bool>, List<Info>, Info> best = (match, source) =>
                source.Where(i => !chosen.Contains(i)).Where(match).OrderByDescending(i => i.Score).FirstOrDefault();

            if (pos == PositionRules.Goalkeeper)
            {
                return best(i => i.Primary == PositionRules.Goalkeeper, club);   // a club with one keeper stays at one
            }

            // 1. a player of the club who can play the position as a secondary position
            var found = best(i => i.Primary != PositionRules.Goalkeeper && i.Secondary.Contains(pos), club);
            if (found != null) return found;
            // 2. the substitution chain (several missing players start at different entries)
            var chain = Chains[pos];
            for (var n = 0; n < chain.Length; n++)
            {
                var sub = chain[(k + n) % chain.Length];
                found = best(i => i.Primary == sub, club);
                if (found != null) return found;
            }
            // 3. anybody else of the club
            return best(i => i.Primary != PositionRules.Goalkeeper, club);
        }
    }
}