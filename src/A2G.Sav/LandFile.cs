using System.Collections.Generic;
using System.Linq;

namespace A2G.Sav
{
    /// <summary>
    /// One team of a Land*.sav file:
    /// 4 name lines (name, short name or "---", city, nickname), a trainer block (2 names + 3 ints),
    /// a second staff block (2 names + 21 ints, meaning not yet decoded) and the players (15 lines each).
    /// </summary>
    public sealed class LandTeam
    {
        public const int TrainerValueCount = 3;
        public const int StaffBlockValueCount = 21;

        public string Name { get; set; }
        /// <summary>Short name, or "---" when the team has none.</summary>
        public string ShortName { get; set; }
        public string City { get; set; }
        public string Nickname { get; set; }
        public PersonRecord Trainer { get; set; }
        /// <summary>Second staff block (2 names + 21 ints). Kept verbatim until its fields are decoded.</summary>
        public PersonRecord StaffBlock { get; set; }
        public List<Player> Players { get; private set; }

        public LandTeam()
        {
            Players = new List<Player>();
        }

        internal static LandTeam Read(LineReader r)
        {
            var t = new LandTeam();
            for (var i = 0; i < 4; i++)
                if (!r.StringAhead(i))
                    throw r.Error("a team header needs 4 text lines");
            t.Name = r.ReadString();
            t.ShortName = r.ReadString();
            t.City = r.ReadString();
            t.Nickname = r.ReadString();
            t.Trainer = PersonRecord.Read(r, TrainerValueCount);
            t.StaffBlock = PersonRecord.Read(r, StaffBlockValueCount);
            while (Player.LooksLikeAt(r)) t.Players.Add(Player.Read(r));
            return t;
        }

        internal void Write(List<string> o)
        {
            o.Add(Name);
            o.Add(ShortName);
            o.Add(City);
            o.Add(Nickname);
            Trainer.Write(o);
            StaffBlock.Write(o);
            foreach (var p in Players) p.Write(o);
        }
    }

    /// <summary>
    /// LandDeut.sav, LANDENGL.SAV, LANDFRAN.SAV, LANDITAL.SAV, LANDSPAN.SAV.
    /// Header: magic, number of leagues, number of teams and three further numbers (meaning unknown).
    /// Then the teams, then a tail of spare club names followed by one more person block.
    /// </summary>
    public sealed class LandFile : SavFile
    {
        public int Magic { get; set; }
        /// <summary>Number of leagues stored in the file (Germany 6, England 3, France 2, Italy 1, Spain 1).</summary>
        public int LeagueCount { get; set; }
        public int HeaderA { get; set; }
        public int HeaderB { get; set; }
        public int HeaderC { get; set; }
        public List<LandTeam> Teams { get; private set; }
        /// <summary>Spare club names listed after the last team (40 in the original files).</summary>
        public List<string> TailClubNames { get; private set; }
        public PersonRecord TailPerson { get; set; }

        public LandFile()
        {
            Teams = new List<LandTeam>();
            TailClubNames = new List<string>();
            Magic = SavConstants.Magic;
        }

        public static LandFile Parse(IList<string> lines, string fileName)
        {
            var r = new LineReader(lines, fileName);
            var f = new LandFile();
            f.Magic = r.ReadInt();
            f.LeagueCount = r.ReadInt();
            var teamCount = r.ReadInt();
            f.HeaderA = r.ReadInt();
            f.HeaderB = r.ReadInt();
            f.HeaderC = r.ReadInt();
            for (var i = 0; i < teamCount; i++) f.Teams.Add(LandTeam.Read(r));

            // Tail: spare club names, then a person block of 2 names + 3 ints (5 lines).
            const int tailPersonLines = 5;
            if (r.Remaining < tailPersonLines) throw r.Error("tail is missing");
            while (r.Remaining > tailPersonLines) f.TailClubNames.Add(r.ReadString());
            f.TailPerson = PersonRecord.Read(r, 3);
            return f;
        }

        public override void WriteLines(List<string> o)
        {
            o.Add(SavText.Format(Magic));
            o.Add(SavText.Format(LeagueCount));
            o.Add(SavText.Format(Teams.Count));
            o.Add(SavText.Format(HeaderA));
            o.Add(SavText.Format(HeaderB));
            o.Add(SavText.Format(HeaderC));
            foreach (var t in Teams) t.Write(o);
            o.AddRange(TailClubNames);
            TailPerson.Write(o);
        }

        public override string Describe()
        {
            return string.Format("Land: {0} leagues, {1} teams, {2} players, {3} spare clubs",
                LeagueCount, Teams.Count, Teams.Sum(t => t.Players.Count), TailClubNames.Count);
        }
    }
}
