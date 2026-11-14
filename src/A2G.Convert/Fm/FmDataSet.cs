using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using A2G.Convert.Csv;

namespace A2G.Convert.Fm
{
    /// <summary>
    /// The FM data needed for a conversion, loaded from the CSV files in a project's fm_export folder:
    /// clubs.csv, players.csv, nations.csv (all from "fmsave export"), nation_names.csv (from
    /// "fmsave a2g-nations") and optionally fixtures.csv (used to find the leagues).
    /// </summary>
    public sealed class FmDataSet
    {
        private static readonly string[] PositionCodes =
            { "gk", "sw", "dl", "dc", "dr", "dm", "ml", "mc", "mr", "aml", "amc", "amr", "stc", "wbl", "wbr" };

        private readonly Dictionary<string, List<FmPlayer>> _playersByClub = new Dictionary<string, List<FmPlayer>>();
        private readonly Dictionary<int, FmNation> _nationsBySaveId = new Dictionary<int, FmNation>();

        public Dictionary<string, FmClub> Clubs { get; private set; }
        public List<FmPlayer> Players { get; private set; }
        public List<FmNation> Nations { get; private set; }
        public List<FmLeague> Leagues { get; private set; }
        /// <summary>Staff from staff.csv (empty when the file was not exported).</summary>
        public List<FmStaff> Staff { get; private set; }

        private readonly Dictionary<string, List<FmStaff>> _staffByClub = new Dictionary<string, List<FmStaff>>();
        private readonly Dictionary<int, List<FmStaff>> _staffByNation = new Dictionary<int, List<FmStaff>>();

        /// <summary>Staff of a club, best Current Ability first.</summary>
        public IList<FmStaff> StaffOf(FmClub club)
        {
            List<FmStaff> list;
            return _staffByClub.TryGetValue(club.Uid, out list) ? list : (IList<FmStaff>)new FmStaff[0];
        }

        /// <summary>Staff of a nation, best Current Ability first.</summary>
        public IList<FmStaff> StaffOfNation(int nationId)
        {
            List<FmStaff> list;
            return _staffByNation.TryGetValue(nationId, out list) ? list : (IList<FmStaff>)new FmStaff[0];
        }

        private FmDataSet()
        {
            Staff = new List<FmStaff>();
            Clubs = new Dictionary<string, FmClub>();
            Players = new List<FmPlayer>();
            Nations = new List<FmNation>();
            Leagues = new List<FmLeague>();
        }

        public static FmDataSet Load(string folder)
        {
            var set = new FmDataSet();
            set.LoadNations(folder);
            set.LoadClubs(Path.Combine(folder, "clubs.csv"));
            set.LoadPlayers(Path.Combine(folder, "players.csv"));
            var staffPath = Path.Combine(folder, "staff.csv");
            if (File.Exists(staffPath)) set.LoadStaff(staffPath);
            var fixtures = Path.Combine(folder, "fixtures.csv");
            if (File.Exists(fixtures)) set.Leagues = FmLeagueDetector.Detect(fixtures, set);
            var stadiums = Path.Combine(folder, "stadiums.csv");
            if (File.Exists(stadiums)) set.LoadStadiums(stadiums);
            return set;
        }

        /// <summary>True when stadiums.csv was exported (older projects have none: export again to get the stadium sizes of the save).</summary>
        public bool HasStadiumData { get; private set; }

        /// <summary>
        /// Gives every club the capacity of its ground: stadiums.csv lists, per ground, the clubs whose home ground it is
        /// ("home_club_uids", separated by semicolons; a club has at most one ground there).
        /// </summary>
        private void LoadStadiums(string path)
        {
            CsvHeader h = null;
            int iCapacity = -1, iHome = -1;
            foreach (var row in CsvReader.ReadRecords(path))
            {
                if (h == null)
                {
                    h = new CsvHeader(row);
                    iCapacity = h.Has("total_capacity") ? h.IndexOf("total_capacity") : h.Has("all_seater_capacity") ? h.IndexOf("all_seater_capacity") : -1;   // the column was called all_seater_capacity before fmsave 0.10.0
                    iHome = h.Has("home_club_uids") ? h.IndexOf("home_club_uids") : -1;
                    HasStadiumData = iCapacity >= 0 && iHome >= 0;
                    if (!HasStadiumData) return;
                    continue;
                }
                var capacity = CsvHeader.GetInt(row, iCapacity);
                if (capacity <= 0) continue;
                foreach (var uid in CsvHeader.Get(row, iHome).Split(';'))
                {
                    FmClub club;
                    if (Clubs.TryGetValue(uid.Trim(), out club) && capacity > club.StadiumCapacity) club.StadiumCapacity = capacity;
                }
            }
        }

        public FmNation NationBySaveId(int saveId)
        {
            FmNation n;
            return _nationsBySaveId.TryGetValue(saveId, out n) ? n : null;
        }

        public FmNation NationByName(string name)
        {
            return Nations.FirstOrDefault(n => string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        public IList<FmPlayer> PlayersOf(FmClub club)
        {
            List<FmPlayer> list;
            return _playersByClub.TryGetValue(club.Uid, out list) ? list : (IList<FmPlayer>)new FmPlayer[0];
        }

        private Dictionary<int, List<FmPlayer>> _freeAgents;

        /// <summary>Players without a club, best Current Ability first, for padding small squads.</summary>
        public IList<FmPlayer> FreeAgentsOf(int nationId)
        {
            if (_freeAgents == null)
            {
                _freeAgents = new Dictionary<int, List<FmPlayer>>();
                foreach (var p in Players.Where(x => x.ClubUid == null).OrderByDescending(x => x.CurrentAbility))
                {
                    List<FmPlayer> list;
                    if (!_freeAgents.TryGetValue(p.NationId, out list)) _freeAgents[p.NationId] = list = new List<FmPlayer>();
                    list.Add(p);
                }
            }
            List<FmPlayer> result;
            return _freeAgents.TryGetValue(nationId, out result) ? result : (IList<FmPlayer>)new FmPlayer[0];
        }

        private List<FmPlayer> _freeGoalkeepers;

        /// <summary>Goalkeepers without a club from any nation, best first (last resort for squads without keepers).</summary>
        public IList<FmPlayer> FreeGoalkeepers()
        {
            return _freeGoalkeepers ?? (_freeGoalkeepers = Players
                .Where(p => p.ClubUid == null && p.IsGoalkeeper).OrderByDescending(p => p.CurrentAbility).ToList());
        }

        public int SquadSize(FmClub club)
        {
            List<FmPlayer> list;
            return _playersByClub.TryGetValue(club.Uid, out list) ? list.Count : 0;
        }

        // ---- loading ------------------------------------------------------------------------

        private void LoadNations(string folder)
        {
            var names = new Dictionary<int, string[]>();
            var namesPath = Path.Combine(folder, "nation_names.csv");
            if (File.Exists(namesPath))
            {
                var first = true;
                foreach (var row in CsvReader.ReadRecords(namesPath))
                {
                    if (first) { first = false; continue; }
                    if (row.Length < 4) continue;
                    names[int.Parse(row[0], CultureInfo.InvariantCulture)] = new[] { row[1], row[3] };
                }
            }

            var path = Path.Combine(folder, "nations.csv");
            var header = (CsvHeader)null;
            foreach (var row in CsvReader.ReadRecords(path))
            {
                if (header == null) { header = new CsvHeader(row); continue; }
                var saveId = CsvHeader.GetInt(row, header.IndexOf("nation_id"));
                var dbId = CsvHeader.GetInt(row, header.IndexOf("database_id"));
                string[] nm;
                names.TryGetValue(dbId, out nm);
                var nation = new FmNation
                {
                    SaveId = saveId,
                    DatabaseId = dbId,
                    Name = nm != null ? nm[0] : null,
                    Code = nm != null ? nm[1] : null
                };
                Nations.Add(nation);
                _nationsBySaveId[saveId] = nation;
            }
        }

        private static readonly Regex TeamPattern = new Regex(
            "\\{\"team_id\":(\\d+),\"slot\":(\\d+),\"club_uid\":(\\d+),\"is_affiliate\":(true|false)\\}",
            RegexOptions.Compiled);

        private void LoadClubs(string path)
        {
            CsvHeader h = null;
            int iUid = 0, iName = 0, iShort = 0, iNation = 0, iTeams = 0, iParent = 0, iRep = 0, iGender = 0, iLastPos = -1;
            foreach (var row in CsvReader.ReadRecords(path))
            {
                if (h == null)
                {
                    h = new CsvHeader(row);
                    iUid = h.IndexOf("uid"); iName = h.IndexOf("name"); iShort = h.IndexOf("short_name");
                    iNation = h.IndexOf("nation_id"); iTeams = h.IndexOf("teams"); iParent = h.IndexOf("parent_club_uid");
                    iRep = h.IndexOf("reputation"); iGender = h.IndexOf("gender");
                    try { iLastPos = h.IndexOf("last_league_position"); } catch (Exception) { iLastPos = -1; }
                    continue;
                }
                var club = new FmClub
                {
                    Uid = row[iUid],
                    Name = row[iName],
                    ShortName = CsvHeader.Get(row, iShort),
                    NationId = CsvHeader.GetInt(row, iNation, -1),
                    Reputation = CsvHeader.GetInt(row, iRep),
                    LastLeaguePosition = iLastPos >= 0 ? CsvHeader.GetInt(row, iLastPos) : 0,
                    FirstTeamId = -1
                };
                foreach (Match m in TeamPattern.Matches(CsvHeader.Get(row, iTeams)))
                {
                    if (m.Groups[2].Value == "0" && m.Groups[4].Value == "false")
                        club.FirstTeamId = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                }
                club.IsMainMensClub = string.Equals(CsvHeader.Get(row, iGender), "men", StringComparison.OrdinalIgnoreCase)
                                      && string.IsNullOrEmpty(CsvHeader.Get(row, iParent));
                Clubs[club.Uid] = club;
            }
        }

        private void LoadStaff(string path)
        {
            CsvHeader h = null;
            int iUid = 0, iFirst = 0, iLast = 0, iName = 0, iAge = 0, iNation = 0, iClub = 0, iCa = 0, iHard = -1, iTemp = -1;
            foreach (var row in CsvReader.ReadRecords(path))
            {
                if (h == null)
                {
                    h = new CsvHeader(row);
                    iUid = h.IndexOf("uid"); iFirst = h.IndexOf("first_name"); iLast = h.IndexOf("last_name"); iName = h.IndexOf("name");
                    iAge = h.IndexOf("age"); iNation = h.IndexOf("nation_id"); iClub = h.IndexOf("club_uid"); iCa = h.IndexOf("ability_current");
                    iHard = h.Has("preferences_hardness_of_training") ? h.IndexOf("preferences_hardness_of_training") : -1;
                    iTemp = h.Has("personality_temperament") ? h.IndexOf("personality_temperament") : -1;
                    continue;
                }
                var clubUid = CsvHeader.Get(row, iClub);
                var s = new FmStaff
                {
                    Uid = row[iUid], FirstName = CsvHeader.Get(row, iFirst), LastName = CsvHeader.Get(row, iLast),
                    DisplayName = CsvHeader.Get(row, iName), Age = CsvHeader.GetInt(row, iAge), NationId = CsvHeader.GetInt(row, iNation, -1),
                    ClubUid = clubUid.Length == 0 ? null : clubUid, CurrentAbility = CsvHeader.GetInt(row, iCa),
                    TrainingHardness = iHard >= 0 ? CsvHeader.GetInt(row, iHard) : 0, Temperament = iTemp >= 0 ? CsvHeader.GetInt(row, iTemp) : 0
                };
                Staff.Add(s);
            }
            foreach (var s in Staff.OrderByDescending(x => x.CurrentAbility).ThenBy(x => x.Uid, StringComparer.Ordinal))
            {
                List<FmStaff> list;
                if (s.ClubUid != null)
                {
                    if (!_staffByClub.TryGetValue(s.ClubUid, out list)) _staffByClub[s.ClubUid] = list = new List<FmStaff>();
                    list.Add(s);
                }
                if (!_staffByNation.TryGetValue(s.NationId, out list)) _staffByNation[s.NationId] = list = new List<FmStaff>();
                list.Add(s);
            }
        }

        /// <summary>Trait names from the save; where fmsave does not know the name only the code is available.</summary>
        private static List<string> ReadTraits(string names, string codes)
        {
            var n = names.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            var c = codes.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            var result = new List<string>();
            for (var i = 0; i < Math.Max(n.Length, c.Length); i++)
            {
                var name = i < n.Length ? n[i].Trim() : string.Empty;
                if (name.Length == 0 || name.Equals("unknown", StringComparison.OrdinalIgnoreCase))
                    name = i < c.Length ? "Trait " + c[i].Trim() : string.Empty;
                if (name.Length > 0) result.Add(name);
            }
            return result;
        }

        private void LoadPlayers(string path)
        {
            CsvHeader h = null;
            var attrIdx = new List<KeyValuePair<string, int>>();
            var posIdx = new List<KeyValuePair<string, int>>();
            int iUid = 0, iFirst = 0, iLast = 0, iCommon = 0, iName = 0, iAge = 0, iNation = 0, iCa = 0, iPa = 0, iClub = 0, iTeam = 0, iHeight = 0;
            int iBirth = -1, iRepCur = -1, iRepWorld = -1, iTraits = -1, iTraitCodes = -1;

            foreach (var row in CsvReader.ReadRecords(path))
            {
                if (h == null)
                {
                    h = new CsvHeader(row);
                    iUid = h.IndexOf("uid"); iFirst = h.IndexOf("first_name"); iLast = h.IndexOf("last_name");
                    iCommon = h.IndexOf("common_name"); iName = h.IndexOf("name"); iAge = h.IndexOf("age");
                    iNation = h.IndexOf("nation_id"); iCa = h.IndexOf("ability_current"); iPa = h.IndexOf("ability_potential");
                    iClub = h.IndexOf("club_uid"); iTeam = h.IndexOf("team_id"); iHeight = h.IndexOf("height_cm");
                    iBirth = h.Has("birth_date") ? h.IndexOf("birth_date") : -1;
                    iRepCur = h.Has("reputation_current") ? h.IndexOf("reputation_current") : -1;
                    iRepWorld = h.Has("reputation_world") ? h.IndexOf("reputation_world") : -1;
                    iTraits = h.Has("traits") ? h.IndexOf("traits") : -1;
                    iTraitCodes = h.Has("traits_code") ? h.IndexOf("traits_code") : -1;
                    for (var i = 0; i < row.Length; i++)
                    {
                        if (row[i].StartsWith("attributes_", StringComparison.Ordinal))
                            attrIdx.Add(new KeyValuePair<string, int>(row[i].Substring("attributes_".Length), i));
                        else if (row[i].StartsWith("personality_", StringComparison.Ordinal))
                            attrIdx.Add(new KeyValuePair<string, int>(row[i].Substring("personality_".Length), i));
                        else if (row[i] == "left_foot" || row[i] == "right_foot")
                            attrIdx.Add(new KeyValuePair<string, int>(row[i], i));
                        else if (row[i].StartsWith("positions_", StringComparison.Ordinal))
                            posIdx.Add(new KeyValuePair<string, int>(row[i].Substring("positions_".Length), i));
                    }
                    continue;
                }

                var clubUid = CsvHeader.Get(row, iClub);
                var player = new FmPlayer
                {
                    Uid = row[iUid],
                    FirstName = CsvHeader.Get(row, iFirst),
                    LastName = CsvHeader.Get(row, iLast),
                    CommonName = CsvHeader.Get(row, iCommon),
                    DisplayName = CsvHeader.Get(row, iName),
                    Age = CsvHeader.GetInt(row, iAge),
                    NationId = CsvHeader.GetInt(row, iNation, -1),
                    CurrentAbility = CsvHeader.GetInt(row, iCa),
                    PotentialAbility = CsvHeader.GetInt(row, iPa),
                    ClubUid = clubUid.Length == 0 ? null : clubUid,
                    TeamId = CsvHeader.GetInt(row, iTeam, -1),
                    HeightCm = CsvHeader.GetInt(row, iHeight),
                    BirthDate = iBirth >= 0 ? CsvHeader.Get(row, iBirth) : string.Empty,
                    ReputationCurrent = iRepCur >= 0 ? CsvHeader.GetInt(row, iRepCur) : 0,
                    ReputationWorld = iRepWorld >= 0 ? CsvHeader.GetInt(row, iRepWorld) : 0,
                    Traits = ReadTraits(iTraits >= 0 ? CsvHeader.Get(row, iTraits) : string.Empty,
                        iTraitCodes >= 0 ? CsvHeader.Get(row, iTraitCodes) : string.Empty),
                    Attributes = new Dictionary<string, int>(attrIdx.Count),
                    PositionRatings = new Dictionary<string, int>(posIdx.Count)
                };
                foreach (var kv in attrIdx) player.Attributes[kv.Key] = CsvHeader.GetInt(row, kv.Value);
                foreach (var kv in posIdx) player.PositionRatings[kv.Key] = CsvHeader.GetInt(row, kv.Value);

                Players.Add(player);
                if (player.ClubUid != null)
                {
                    List<FmPlayer> list;
                    if (!_playersByClub.TryGetValue(player.ClubUid, out list))
                        _playersByClub[player.ClubUid] = list = new List<FmPlayer>();
                    list.Add(player);
                }
            }
        }
    }
}
