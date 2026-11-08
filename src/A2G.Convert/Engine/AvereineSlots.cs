using System.Collections.Generic;
using System.Linq;
using A2G.Convert.Fm;

namespace A2G.Convert.Engine
{
    /// <summary>
    /// The nations of the non-league clubs of Anstoss 2. The game groups the clubs of Avereine.sav by nation and expects the file
    /// sorted by nation (LAENDER index): 130 clubs of 30 European nations (3 to 6 each) and, as "spare club names", 40 overseas
    /// clubs of 32 nations. The shipped file has exactly this layout and the converter keeps it: a different nation list or an
    /// unsorted file (as an earlier version wrote it) made the game abort while loading a new game.
    /// </summary>
    public static class AvereineSlots
    {
        public sealed class Slot
        {
            public string Nation;   // A2 nation name (LAENDER.SAV)
            public int Count;
            public Slot(string nation, int count) { Nation = nation; Count = count; }
        }

        /// <summary>The 130 clubs of Avereine.sav, in LAENDER order.</summary>
        public static readonly Slot[] Clubs =
        {
            new Slot("Belgien", 6), new Slot("Bulgarien", 4), new Slot("Dänemark", 6), new Slot("Finnland", 4), new Slot("Griechenland", 6),
            new Slot("Holland", 6), new Slot("Irland", 3), new Slot("Island", 4), new Slot("Israel", 4), new Slot("Kroatien", 4),
            new Slot("Luxemburg", 3), new Slot("Malta", 3), new Slot("Nordirland", 3), new Slot("Norwegen", 4), new Slot("Österreich", 6),
            new Slot("Polen", 4), new Slot("Portugal", 6), new Slot("Rumänien", 6), new Slot("Rußland", 6), new Slot("Schottland", 6),
            new Slot("Schweden", 4), new Slot("Schweiz", 4), new Slot("Slowakei", 3), new Slot("Slowenien", 3), new Slot("Tschechien", 3),
            new Slot("Türkei", 6), new Slot("Ukraine", 3), new Slot("Ungarn", 4), new Slot("Wales", 3), new Slot("Zypern", 3)
        };

        /// <summary>The 40 spare club names of Avereine.sav (name and nation only), in LAENDER order.</summary>
        public static readonly Slot[] Extras =
        {
            new Slot("Ägypten", 1), new Slot("Algerien", 1), new Slot("Argentinien", 3), new Slot("Australien", 1), new Slot("Bolivien", 1),
            new Slot("Brasilien", 5), new Slot("Chile", 1), new Slot("China", 1), new Slot("Costa Rica", 1), new Slot("Ecuador", 1),
            new Slot("Ghana", 1), new Slot("Jamaika", 1), new Slot("Japan", 1), new Slot("Kamerun", 1), new Slot("Kanada", 1),
            new Slot("Kolumbien", 2), new Slot("Liberia", 1), new Slot("Marokko", 1), new Slot("Mexiko", 2), new Slot("Neuseeland", 1),
            new Slot("Nigeria", 1), new Slot("Paraguay", 1), new Slot("Peru", 1), new Slot("Puerto Rico", 1), new Slot("Saudi-Arabien", 1),
            new Slot("Senegal", 1), new Slot("Südkorea", 1), new Slot("Tunesien", 1), new Slot("Uruguay", 1), new Slot("USA", 1),
            new Slot("Venezuela", 1), new Slot("Zaire", 1)
        };

        /// <summary>FM nation ids that belong to an A2 nation name.</summary>
        public static List<int> NationIds(FmDataSet fm, string a2Nation)
        {
            var ids = new List<int>();
            foreach (var name in A2Nations.FmNamesOf(a2Nation))
            {
                var n = fm.NationByName(name);
                if (n != null && !ids.Contains(n.SaveId)) ids.Add(n.SaveId);
            }
            return ids;
        }

        /// <summary>Index of the slot of the club's nation in <paramref name="slots"/>, or -1 when the nation has no slot.</summary>
        public static int SlotOf(FmDataSet fm, FmClub club, Slot[] slots)
        {
            for (var i = 0; i < slots.Length; i++)
                if (NationIds(fm, slots[i].Nation).Contains(club.NationId)) return i;
            return -1;
        }

        public static bool IsClubNation(FmDataSet fm, FmClub club) { return SlotOf(fm, club, Clubs) >= 0; }

        /// <summary>
        /// Takes the best clubs (candidates are in the order of preference) for every slot; when a nation has too few clubs the
        /// missing ones are taken from the best remaining clubs of any slot nation. The result is ordered by slot, then by preference.
        /// </summary>
        public static List<FmClub> Pick(List<FmClub> candidates, Slot[] slots, FmDataSet fm, List<string> warnings, string what)
        {
            var slotOf = new Dictionary<FmClub, int>();
            foreach (var c in candidates) { var s = SlotOf(fm, c, slots); if (s >= 0) slotOf[c] = s; }
            var picked = new List<FmClub>();
            var used = new HashSet<FmClub>();
            var missing = 0;
            for (var i = 0; i < slots.Length; i++)
            {
                var take = candidates.Where(c => slotOf.ContainsKey(c) && slotOf[c] == i).Take(slots[i].Count).ToList();
                foreach (var c in take) { picked.Add(c); used.Add(c); }
                if (take.Count < slots[i].Count)
                {
                    missing += slots[i].Count - take.Count;
                    warnings.Add(what + ": only " + take.Count + " of " + slots[i].Count + " clubs found for " + slots[i].Nation + ".");
                }
            }
            // nations without enough FM clubs keep their shipped clubs in the writer; no clubs of other nations fill the gaps
            return picked.OrderBy(c => slotOf[c]).ThenBy(c => candidates.IndexOf(c)).ToList();
        }
    }
}
