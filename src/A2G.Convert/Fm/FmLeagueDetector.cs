using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using A2G.Convert.Csv;

namespace A2G.Convert.Fm
{
    /// <summary>
    /// Finds the leagues of the FM save without any name table: a league is a competition in which the first teams
    /// of 8 or more clubs meet each other twice (home and away). A nation's leagues are ranked by the average
    /// reputation of their clubs, which gives the division ladder (tier 1 = top division).
    /// </summary>
    public static class FmLeagueDetector
    {
        public static List<FmLeague> Detect(string fixturesPath, FmDataSet data)
        {
            var teamToClub = new Dictionary<int, FmClub>();
            foreach (var club in data.Clubs.Values)
                if (club.FirstTeamId >= 0 && club.IsMainMensClub) teamToClub[club.FirstTeamId] = club;

            // season -> number of fixtures, to pick the season the save is in
            CsvHeader h = null;
            int iSeason = 0, iComp = 0, iHome = 0, iAway = 0;
            var rows = new List<int[]>(); // season, competition, homeTeam, awayTeam
            var seasonCounts = new Dictionary<int, int>();
            foreach (var row in CsvReader.ReadRecords(fixturesPath))
            {
                if (h == null)
                {
                    h = new CsvHeader(row);
                    iSeason = h.IndexOf("season_start_year"); iComp = h.IndexOf("competition_id");
                    iHome = h.IndexOf("home_team_id"); iAway = h.IndexOf("away_team_id");
                    continue;
                }
                int season;
                if (!int.TryParse(CsvHeader.Get(row, iSeason), NumberStyles.Integer, CultureInfo.InvariantCulture, out season)) continue;
                var home = CsvHeader.GetInt(row, iHome, -1);
                var away = CsvHeader.GetInt(row, iAway, -1);
                if (!teamToClub.ContainsKey(home) || !teamToClub.ContainsKey(away)) continue;
                rows.Add(new[] { season, CsvHeader.GetInt(row, iComp, -1), home, away });
                int c;
                seasonCounts.TryGetValue(season, out c);
                seasonCounts[season] = c + 1;
            }
            if (seasonCounts.Count == 0) return new List<FmLeague>();
            var currentSeason = seasonCounts.OrderByDescending(kv => kv.Value).First().Key;

            // competition -> unordered club pair -> number of meetings
            var meetings = new Dictionary<int, Dictionary<long, int>>();
            foreach (var r in rows)
            {
                if (r[0] != currentSeason || r[2] == r[3]) continue;
                var a = Math.Min(r[2], r[3]);
                var b = Math.Max(r[2], r[3]);
                Dictionary<long, int> pairs;
                if (!meetings.TryGetValue(r[1], out pairs)) meetings[r[1]] = pairs = new Dictionary<long, int>();
                var key = ((long)a << 32) | (uint)b;
                int n;
                pairs.TryGetValue(key, out n);
                pairs[key] = n + 1;
            }

            var leagues = new List<FmLeague>();
            foreach (var kv in meetings)
            {
                var pairs = kv.Value;
                var teamIds = new HashSet<int>();
                foreach (var key in pairs.Keys) { teamIds.Add((int)(key >> 32)); teamIds.Add((int)(key & 0xFFFFFFFF)); }
                var n = teamIds.Count;
                if (n < 8) continue;
                var doubled = pairs.Values.Count(v => v == 2);
                if (doubled < 0.9 * pairs.Count || pairs.Count < 0.8 * n * (n - 1) / 2.0) continue;

                var clubs = teamIds.Select(t => teamToClub[t]).Distinct().ToList();
                var nation = clubs.GroupBy(c => c.NationId).OrderByDescending(g => g.Count()).First().Key;
                leagues.Add(new FmLeague
                {
                    CompetitionId = kv.Key,
                    NationId = nation,
                    Clubs = clubs,
                    AverageReputation = clubs.Average(c => (double)c.Reputation)
                });
            }

            foreach (var group in leagues.GroupBy(l => l.NationId))
            {
                var tier = 1;
                foreach (var league in group.OrderByDescending(l => l.AverageReputation)) league.Tier = tier++;
            }
            return leagues.OrderBy(l => l.NationId).ThenBy(l => l.Tier).ToList();
        }
    }
}
