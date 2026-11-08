using System;
using System.Collections.Generic;
using System.Linq;
using A2G.Convert.Fm;

namespace A2G.Convert.Engine
{
    /// <summary>
    /// How FM position ratings (0..20) become Anstoss 2 positions. A2 positions: 1 Torwart, 2 Libero, 3 Manndecker,
    /// 4 linker / 5 rechter Verteidiger, 6 defensives Mittelfeld, 7 linkes / 8 rechtes Mittelfeld, 9 offensives Mittelfeld
    /// (every position in the middle of the midfield except the defensive one: FM MC and AMC), 10 Stuermer.
    /// </summary>
    public static class PositionRules
    {
        public const int Goalkeeper = 1, Libero = 2, CentralDefender = 3, LeftBack = 4, RightBack = 5,
            DefensiveMidfield = 6, LeftMidfield = 7, RightMidfield = 8, OffensiveMidfield = 9, Striker = 10;

        /// <summary>Order that breaks ties between equal ratings.</summary>
        public static readonly int[] Priority = { 1, 10, 9, 6, 7, 8, 4, 5, 3, 2 };

        /// <summary>The rating of a player for each A2 position (1..10).</summary>
        public static Dictionary<int, int> Scores(FmPlayer p)
        {
            Func<string, int> r = code => { int v; return p.PositionRatings != null && p.PositionRatings.TryGetValue(code, out v) ? v : 0; };
            return new Dictionary<int, int>
            {
                { 1, r("gk") },
                { 2, r("sw") },
                { 3, r("dc") },
                { 4, Math.Max(r("dl"), r("wbl")) },
                { 5, Math.Max(r("dr"), r("wbr")) },
                { 6, r("dm") },
                { 7, Math.Max(r("ml"), r("aml")) },
                { 8, Math.Max(r("mr"), r("amr")) },
                { 9, Math.Max(r("amc"), r("mc")) },
                { 10, r("stc") }
            };
        }

        /// <summary>The best position; a player without any rating becomes a defensive midfielder.</summary>
        public static int Primary(Dictionary<int, int> scores)
        {
            var ordered = Priority.OrderByDescending(pos => scores[pos]).ToList();   // stable: priority breaks ties
            return scores[ordered[0]] > 0 ? ordered[0] : DefensiveMidfield;
        }

        /// <summary>Up to two other positions (never goalkeeper) rated at least <paramref name="minRating"/>; none for goalkeepers.</summary>
        public static List<int> Secondary(Dictionary<int, int> scores, int primary, int minRating)
        {
            if (primary == Goalkeeper) return new List<int>();
            return Priority.OrderByDescending(pos => scores[pos])
                .Where(pos => pos != primary && pos != Goalkeeper && scores[pos] >= minRating).Take(2).ToList();
        }
    }
}
