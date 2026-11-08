using System.Collections.Generic;
using System.Linq;

namespace A2G.Sav
{
    /// <summary>
    /// A non-league club of Avereine.sav: 4 text lines (name, short name, stadium, city), 11 integers
    /// (home/away shirt, shorts and socks colours, country, stadium capacity, financial power, article
    /// type, flag) and exactly 11 players in the original file.
    /// </summary>
    public sealed class AvereineTeam
    {
        public const int ValueCount = 11;

        public string Name { get; set; }
        public string ShortName { get; set; }
        public string Stadium { get; set; }
        public string City { get; set; }
        /// <summary>Raw header integers; see the named accessors below.</summary>
        public int[] Values { get; set; }
        public List<Player> Players { get; private set; }

        public AvereineTeam()
        {
            Values = new int[ValueCount];
            Players = new List<Player>();
        }

        public int HomeShirt { get { return Values[0]; } set { Values[0] = value; } }
        public int HomeShorts { get { return Values[1]; } set { Values[1] = value; } }
        public int HomeSocks { get { return Values[2]; } set { Values[2] = value; } }
        public int AwayShirt { get { return Values[3]; } set { Values[3] = value; } }
        public int AwayShorts { get { return Values[4]; } set { Values[4] = value; } }
        public int AwaySocks { get { return Values[5]; } set { Values[5] = value; } }
        /// <summary>Index into LAENDER.SAV.</summary>
        public int Country { get { return Values[6]; } set { Values[6] = value; } }
        public int StadiumCapacity { get { return Values[7]; } set { Values[7] = value; } }
        /// <summary>-1 low, 0 medium, 1 high (the original data only has 0 and -1).</summary>
        public int FinancialPower { get { return Values[8]; } set { Values[8] = value; } }
        public int ArticleType { get { return Values[9]; } set { Values[9] = value; } }
        public int Flag { get { return Values[10]; } set { Values[10] = value; } }

        internal static bool LooksLikeAt(LineReader r)
        {
            if (r.Remaining < 4 + ValueCount) return false;
            for (var i = 0; i < 4; i++) if (!r.StringAhead(i)) return false;
            for (var i = 4; i < 4 + ValueCount; i++) if (!r.IntAhead(i)) return false;
            return true;
        }

        internal static AvereineTeam Read(LineReader r)
        {
            var t = new AvereineTeam();
            t.Name = r.ReadString();
            t.ShortName = r.ReadString();
            t.Stadium = r.ReadString();
            t.City = r.ReadString();
            t.Values = r.ReadInts(ValueCount);
            while (Player.LooksLikeAt(r)) t.Players.Add(Player.Read(r));
            return t;
        }

        internal void Write(List<string> o)
        {
            o.Add(Name);
            o.Add(ShortName);
            o.Add(Stadium);
            o.Add(City);
            foreach (var v in Values) o.Add(SavText.Format(v));
            foreach (var p in Players) p.Write(o);
        }
    }

    /// <summary>A spare club entry after the teams in Avereine.sav: a name and a country index.</summary>
    public sealed class AvereineExtraClub
    {
        public string Name { get; set; }
        public int Country { get; set; }
    }

    /// <summary>
    /// Avereine.sav: magic, then the non-league clubs (no count line), then a tail of
    /// 40 spare clubs (name + country) and 30 integer triplets (country id + two values, meaning unknown).
    /// </summary>
    public sealed class AvereineFile : SavFile
    {
        public int Magic { get; set; }
        public List<AvereineTeam> Teams { get; private set; }
        public List<AvereineExtraClub> ExtraClubs { get; private set; }
        public List<int[]> CountryTriplets { get; private set; }

        public AvereineFile()
        {
            Magic = SavConstants.Magic;
            Teams = new List<AvereineTeam>();
            ExtraClubs = new List<AvereineExtraClub>();
            CountryTriplets = new List<int[]>();
        }

        public static AvereineFile Parse(IList<string> lines, string fileName)
        {
            var r = new LineReader(lines, fileName);
            var f = new AvereineFile();
            f.Magic = r.ReadInt();
            while (AvereineTeam.LooksLikeAt(r)) f.Teams.Add(AvereineTeam.Read(r));

            while (r.StringAhead(0))
            {
                var c = new AvereineExtraClub();
                c.Name = r.ReadString();
                c.Country = r.ReadInt();
                f.ExtraClubs.Add(c);
            }
            if (r.Remaining % 3 != 0) throw r.Error("the country triplets at the end are incomplete");
            while (!r.AtEnd) f.CountryTriplets.Add(r.ReadInts(3));
            return f;
        }

        public override void WriteLines(List<string> o)
        {
            o.Add(SavText.Format(Magic));
            foreach (var t in Teams) t.Write(o);
            foreach (var c in ExtraClubs)
            {
                o.Add(c.Name);
                o.Add(SavText.Format(c.Country));
            }
            foreach (var triplet in CountryTriplets)
                foreach (var v in triplet) o.Add(SavText.Format(v));
        }

        public override string Describe()
        {
            return string.Format("Avereine: {0} clubs, {1} players, {2} spare clubs, {3} triplets",
                Teams.Count, Teams.Sum(t => t.Players.Count), ExtraClubs.Count, CountryTriplets.Count);
        }
    }
}
