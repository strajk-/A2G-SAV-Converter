using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using A2G.Convert.Engine;
using A2G.Convert.Fm;

namespace A2G.Convert.Plan
{
    public enum EditResult { Ok, TooYoung, TargetFull, SourceTooSmall, TeamTooSmall }

    /// <summary>The outcome of a limit-aware player edit.</summary>
    public sealed class EditCheck
    {
        public EditResult Result { get; set; }
        /// <summary>The team the message is about (the target team, or the source team when it would become too small).</summary>
        public TeamPlan Team { get; set; }
        /// <summary>The limit that was hit (age, maximum or minimum squad size).</summary>
        public int Limit { get; set; }
        /// <summary>The team the player moved away from, when he was in another squad.</summary>
        public TeamPlan Source { get; set; }
        /// <summary>The player who left the full target team in his place.</summary>
        public FmPlayer Replaced { get; set; }
        public bool Ok { get { return Result == EditResult.Ok; } }
    }
    /// <summary>A list of team slots the editor shows: a league, the spare teams of a country or the non-league clubs.</summary>
    public sealed class TeamGroup
    {
        public string Key { get; set; }
        public string Name { get; set; }
        /// <summary>Country key (DEU, ENG, ...) or "AVE" for the non-league clubs.</summary>
        public string Country { get; set; }
        public List<TeamPlan> Teams { get; set; }
        /// <summary>Players per team: 16..30 for the Land files, exactly 11 for the non-league clubs.</summary>
        public int MinSquad { get; set; }
        public int MaxSquad { get; set; }
        /// <summary>The league of a playable league group (null for the non-league clubs and the spare teams) and its division (0 = top).</summary>
        public A2LeagueSpec League { get; set; }
        public A2CountrySpec CountrySpec { get; set; }
        public int Tier { get; set; }

        public override string ToString() { return Name; }
    }

    /// <summary>One recorded edit, stored with club and player ids so it can be re-applied to a rebuilt plan.</summary>
    [DataContract]
    public sealed class EditOp
    {
        /// <summary>"swap", "replace", "addPlayer" or "removePlayer".</summary>
        [DataMember] public string Kind { get; set; }
        /// <summary>swap/replace: the first club; add/removePlayer: the club of the team.</summary>
        [DataMember] public string A { get; set; }
        /// <summary>swap: the second club; replace: the pool club; add/removePlayer: the player id.</summary>
        [DataMember] public string B { get; set; }
    }

    [DataContract]
    public sealed class EditLog
    {
        [DataMember] public List<EditOp> Ops { get; set; }
        public EditLog() { Ops = new List<EditOp>(); }

        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var stream = File.Create(path))
            using (var writer = JsonReaderWriterFactory.CreateJsonWriter(stream, Encoding.UTF8, true, true, "  "))
                new DataContractJsonSerializer(typeof(EditLog)).WriteObject(writer, this);
        }

        public static EditLog Load(string path)
        {
            if (!File.Exists(path)) return new EditLog();
            using (var stream = File.OpenRead(path))
                return (EditLog)new DataContractJsonSerializer(typeof(EditLog)).ReadObject(stream);
        }
    }

    /// <summary>
    /// Manual changes to a <see cref="ConversionPlan"/>: swapping teams between leagues, swapping a team for an unused FM
    /// club, and moving players. League sizes never change (every operation is a swap or a replacement). All edits are
    /// recorded in <see cref="Log"/>, which can be saved with the project and applied again after the plan is rebuilt.
    /// </summary>
    public sealed class PlanEditor
    {
        private readonly ConversionPlan _plan;
        private readonly FmDataSet _fm;
        private readonly ConversionSettings _settings;
        private bool _replaying;

        public PlanEditor(ConversionPlan plan, FmDataSet fm, ConversionSettings settings)
        {
            _plan = plan;
            _fm = fm;
            _settings = settings;
            Log = new EditLog();
        }

        public EditLog Log { get; private set; }

        // ---- groups and pool ----------------------------------------------------------------

        public IList<TeamGroup> Groups(string country)
        {
            var groups = new List<TeamGroup>();
            if (country == "AVE")
            {
                groups.Add(new TeamGroup
                {
                    Key = "AVE", Name = "Non-league clubs (Avereine)", Country = "AVE", Teams = _plan.Avereine.Teams,
                    MinSquad = _settings.AvereineSquadSize, MaxSquad = _settings.AvereineSquadSize
                });
                return groups;
            }
            var c = _plan.Countries.FirstOrDefault(x => x.Spec.Key == country);
            if (c == null) return groups;
            var tierNo = 0;
            foreach (var tier in c.Tiers)
            {
                foreach (var league in tier.Leagues)
                    groups.Add(new TeamGroup
                    {
                        Key = c.Spec.Key + ":" + league.Spec.LigaFile, Name = league.Spec.Name, Country = c.Spec.Key,
                        Teams = league.Teams, MinSquad = 16, MaxSquad = 30, League = league.Spec, CountrySpec = c.Spec, Tier = tierNo
                    });
                tierNo++;
            }
            groups.Add(new TeamGroup
            {
                Key = c.Spec.Key + ":spare", Name = "Spare teams", Country = c.Spec.Key, Teams = c.Spares, MinSquad = 16, MaxSquad = 30
            });
            return groups;
        }

        /// <summary>Clubs that may replace a team of the country (or of the non-league group) and are not used yet.</summary>
        public List<FmClub> Pool(string country)
        {
            var used = new HashSet<FmClub>(AllTeams().Select(t => t.Club));
            var mainNations = new HashSet<int>(_plan.Countries
                .Select(c => _fm.NationByName(c.Spec.FmNationName)).Where(n => n != null).Select(n => n.SaveId));
            IEnumerable<FmClub> clubs = _fm.Clubs.Values.Where(c => c.IsMainMensClub && !used.Contains(c));
            if (country == "AVE")
                clubs = clubs.Where(c => !mainNations.Contains(c.NationId) && AvereineSlots.IsClubNation(_fm, c) && _fm.PlayersOf(c).Any(p => SquadSelector.Eligible(p)));   // thin squads are completed from unused players
            else
            {
                var spec = _plan.Countries.FirstOrDefault(c => c.Spec.Key == country);
                var nation = spec == null ? null : _fm.NationByName(spec.Spec.FmNationName);
                if (nation == null) return new List<FmClub>();
                clubs = clubs.Where(c => c.NationId == nation.SaveId && SquadSelector.Qualifies(c, _fm, _settings));
            }
            return clubs.OrderByDescending(c => c.Reputation).ThenBy(c => c.Name, StringComparer.Ordinal).ToList();
        }

        public IEnumerable<TeamPlan> AllTeams()
        {
            return _plan.Countries.SelectMany(c => c.AllTeams).Concat(_plan.Avereine.Teams);
        }

        // ---- team edits ---------------------------------------------------------------------

        /// <summary>Exchanges two teams (for example between two leagues). Both groups must belong to the same country.</summary>
        public void SwapTeams(TeamGroup a, int indexA, TeamGroup b, int indexB)
        {
            if (a.Country != b.Country) throw new InvalidOperationException("Teams can only be swapped within one country.");
            var ta = a.Teams[indexA];
            var tb = b.Teams[indexB];
            a.Teams[indexA] = tb;
            b.Teams[indexB] = ta;
            Record("swap", ta.Club.Uid, tb.Club.Uid);
        }

        /// <summary>Replaces a team by an unused FM club (its default squad is chosen); the old club returns to the pool.</summary>
        public void ReplaceWithPoolClub(TeamGroup group, int index, FmClub poolClub)
        {
            var old = group.Teams[index];
            if (AllTeams().Any(t => t.Club == poolClub)) throw new InvalidOperationException(poolClub.Name + " is already in the plan.");
            if (group.Country == "AVE" && AvereineSlots.SlotOf(_fm, poolClub, AvereineSlots.Clubs) != AvereineSlots.SlotOf(_fm, old.Club, AvereineSlots.Clubs))
                throw new InvalidOperationException(poolClub.Name + ": a non-league club can only be replaced by a club of the same nation (the game's nation layout is fixed).");
            var team = new TeamPlan(poolClub);
            team.Squad.AddRange(group.Country == "AVE"
                ? SquadSelector.SelectAvereine(poolClub, _fm, _settings)
                : SquadSelector.SelectBalanced(poolClub, _fm, _settings));
            if (group.Country == "AVE")
            {
                var used = AllTeams().Where(t => t != old).SelectMany(t => t.Squad).Concat(team.Squad);
                var target = team.Squad.Count > 0 ? team.Squad.Average(p => (double)p.CurrentAbility) : 80.0;
                SquadSelector.TopUpAvereine(team.Squad, poolClub, target, new SquadSelector.AvereinePool(_fm, used), _settings);
            }
            group.Teams[index] = team;
            Record("replace", old.Club.Uid, poolClub.Uid);
        }

        // ---- squad edits --------------------------------------------------------------------

        /// <summary>Adds a player to a team; a player belongs to one team only, so he is taken out of any other squad.</summary>
        public void AddPlayer(TeamPlan team, FmPlayer player)
        {
            if (team.Squad.Contains(player)) return;
            if (!SquadSelector.Eligible(player))
                throw new InvalidOperationException(player.DisplayName + " is younger than " + SquadSelector.MinAge + ": Anstoss 2 players are at least that old.");
            foreach (var other in AllTeams())
                if (other != team) other.Squad.Remove(player);
            team.Squad.Add(player);
            Record("addPlayer", team.Club.Uid, player.Uid);
        }

        // ---- limit-aware player edits (what the screens use; the raw AddPlayer / RemovePlayer above replay saved edits) ----

        /// <summary>The team that currently has the player in its plan squad, or null.</summary>
        public TeamPlan TeamOf(FmPlayer player)
        {
            return AllTeams().FirstOrDefault(t => t.Squad.Contains(player));
        }

        /// <summary>The group (league, spare teams or non-league clubs) a team belongs to, or null.</summary>
        public TeamGroup GroupOf(TeamPlan team)
        {
            var countries = _plan.Countries.Select(c => c.Spec.Key).Concat(new[] { "AVE" });
            return countries.SelectMany(key => Groups(key)).FirstOrDefault(g => g.Teams.Contains(team));
        }

        /// <summary>
        /// Adds a player with the squad limits in mind (16-30 players, exactly 11 for non-league clubs, minimum age 17).
        /// A player belongs to one team only: when he is in another team's squad he moves, which is refused if that team would
        /// fall below its minimum. When the target team is full the player <paramref name="replace"/> leaves in his place.
        /// </summary>
        public EditCheck Add(TeamPlan team, FmPlayer player, FmPlayer replace)
        {
            var result = new EditCheck { Team = team };
            if (team.Squad.Contains(player)) return result;
            if (!SquadSelector.Eligible(player)) { result.Result = EditResult.TooYoung; result.Limit = SquadSelector.MinAge; return result; }

            var group = GroupOf(team);
            if (group == null) return result;
            var full = team.Squad.Count >= group.MaxSquad;
            if (full && (replace == null || !team.Squad.Contains(replace)))
            {
                result.Result = EditResult.TargetFull; result.Limit = group.MaxSquad; return result;
            }

            var source = TeamOf(player);
            if (source != null && source != team)
            {
                var sourceGroup = GroupOf(source);
                if (sourceGroup != null && source.Squad.Count <= sourceGroup.MinSquad)
                {
                    result.Result = EditResult.SourceTooSmall; result.Team = source; result.Limit = sourceGroup.MinSquad; return result;
                }
            }

            if (full)
            {
                RemovePlayer(team, replace);
                result.Replaced = replace;
            }
            AddPlayer(team, player);
            result.Source = source != team ? source : null;
            return result;
        }

        /// <summary>Removes a player unless the team would fall below its minimum squad size.</summary>
        public EditCheck Remove(TeamPlan team, FmPlayer player)
        {
            var result = new EditCheck { Team = team };
            var group = GroupOf(team);
            if (group != null && team.Squad.Contains(player) && team.Squad.Count <= group.MinSquad)
            {
                result.Result = EditResult.TeamTooSmall; result.Limit = group.MinSquad; return result;
            }
            RemovePlayer(team, player);
            return result;
        }
        public void RemovePlayer(TeamPlan team, FmPlayer player)
        {
            if (team.Squad.Remove(player)) Record("removePlayer", team.Club.Uid, player.Uid);
        }

        /// <summary>Problems that would make the written team invalid or unusual for the game.</summary>
        public List<string> Check(TeamGroup group, TeamPlan team)
        {
            var problems = new List<string>();
            var n = team.Squad.Count;
            if (n < group.MinSquad) problems.Add("only " + n + " players (at least " + group.MinSquad + " needed)");
            if (n > group.MaxSquad) problems.Add(n + " players (at most " + group.MaxSquad + ")");
            var keepers = team.Squad.Count(p => p.IsGoalkeeper);
            var need = group.Country == "AVE" ? 1 : _settings.MinGoalkeepers;
            if (keepers < need) problems.Add(keepers + " goalkeeper(s), at least " + need + " needed");
            return problems;
        }

        // ---- overrides ----------------------------------------------------------------------

        /// <summary>Applies recorded edits to this (rebuilt) plan; edits whose clubs or players no longer exist are skipped.</summary>
        public int Replay(EditLog log)
        {
            var applied = 0;
            _replaying = true;
            try
            {
                foreach (var op in log.Ops)
                {
                    if (ApplyOp(op)) { applied++; Log.Ops.Add(op); }
                }
            }
            finally { _replaying = false; }
            return applied;
        }

        private bool ApplyOp(EditOp op)
        {
            TeamGroup groupA, groupB;
            int ia, ib;
            switch (op.Kind)
            {
                case "swap":
                    if (!Find(op.A, out groupA, out ia) || !Find(op.B, out groupB, out ib) || groupA.Country != groupB.Country) return false;
                    SwapTeams(groupA, ia, groupB, ib);
                    return true;
                case "replace":
                    FmClub club;
                    if (!Find(op.A, out groupA, out ia) || !_fm.Clubs.TryGetValue(op.B, out club)) return false;
                    if (AllTeams().Any(t => t.Club == club)) return false;
                    ReplaceWithPoolClub(groupA, ia, club);
                    return true;
                case "addPlayer":
                case "removePlayer":
                    if (!Find(op.A, out groupA, out ia)) return false;
                    var player = _fm.Players.FirstOrDefault(p => p.Uid == op.B);
                    if (player == null) return false;
                    if (op.Kind == "addPlayer") AddPlayer(groupA.Teams[ia], player); else RemovePlayer(groupA.Teams[ia], player);
                    return true;
            }
            return false;
        }

        private bool Find(string clubUid, out TeamGroup group, out int index)
        {
            var countries = _plan.Countries.Select(c => c.Spec.Key).Concat(new[] { "AVE" });
            foreach (var key in countries)
                foreach (var g in Groups(key))
                {
                    var i = g.Teams.FindIndex(t => t.Club.Uid == clubUid);
                    if (i >= 0) { group = g; index = i; return true; }
                }
            group = null;
            index = -1;
            return false;
        }

        private void Record(string kind, string a, string b)
        {
            if (!_replaying) Log.Ops.Add(new EditOp { Kind = kind, A = a, B = b });
        }
    }
}
