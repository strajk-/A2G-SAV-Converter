using System;
using System.Linq;

namespace A2G.Convert.Writing
{
    /// <summary>
    /// Stad*.sav record: name, city, then 29 ints. Ints 0..23 are the capacities of the 24 stadium sections (their sum is the
    /// stadium capacity); the other ints are facilities. Scaling keeps the shape of the template stadium.
    /// </summary>
    public static class StadiumScaler
    {
        public const int Sections = 24, FirstInt = 2, MaxSection = 13500;

        public static int Total(string[] record)
        {
            var sum = 0;
            for (var i = 0; i < Sections; i++) sum += Get(record, i);
            return sum;
        }

        private static int Get(string[] r, int section)
        {
            int v;
            return int.TryParse(r[FirstInt + section], out v) ? Math.Max(0, v) : 0;
        }

        /// <summary>Rescales the section capacities to <paramref name="target"/> (multiples of 100, exact sum where possible).</summary>
        public static void Scale(string[] record, int target)
        {
            var old = Enumerable.Range(0, Sections).Select(i => Get(record, i)).ToArray();
            var total = old.Sum();
            var weights = total > 0 ? old : Enumerable.Repeat(1, Sections).ToArray();
            var wsum = weights.Sum();
            var units = target / 100;                       // work in hundreds
            var result = new int[Sections];
            var assigned = 0;
            for (var i = 0; i < Sections; i++)
            {
                result[i] = Math.Min(MaxSection / 100, (int)Math.Round(units * (double)weights[i] / wsum));
                assigned += result[i];
            }
            // fix the rounding difference on the largest sections
            var order = Enumerable.Range(0, Sections).OrderByDescending(i => weights[i]).ToArray();
            var guard = 0;
            while (assigned != units && guard++ < 10000)
            {
                var step = assigned < units ? 1 : -1;
                var done = false;
                foreach (var i in order)
                {
                    if (weights[i] == 0 && total > 0) continue;
                    var n = result[i] + step;
                    if (n < 0 || n > MaxSection / 100) continue;
                    result[i] = n; assigned += step; done = true;
                    if (assigned == units) break;
                }
                if (!done) break;
            }
            for (var i = 0; i < Sections; i++) record[FirstInt + i] = (result[i] * 100).ToString();
        }
    }
}
