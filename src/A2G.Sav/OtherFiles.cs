using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace A2G.Sav
{
    /// <summary>A nation of LAENDER.SAV. Its index in the file (0 = "Sonstige") is the country id used everywhere else.</summary>
    public sealed class Nation
    {
        public const int ValueCount = 6;

        public string Name { get; set; }
        public int Rating { get; set; }
        /// <summary>Three-letter style code, e.g. "ENG" or "D".</summary>
        public string Code { get; set; }
        /// <summary>Six further numbers whose meaning is not fully documented.</summary>
        public int[] Values { get; set; }
    }

    /// <summary>LAENDER.SAV: magic, then 76 nations of 9 lines each (name, rating, code, 6 numbers).</summary>
    public sealed class LaenderFile : SavFile
    {
        public int Magic { get; set; }
        public List<Nation> Nations { get; private set; }

        public LaenderFile()
        {
            Magic = SavConstants.Magic;
            Nations = new List<Nation>();
        }

        public static LaenderFile Parse(IList<string> lines, string fileName)
        {
            var r = new LineReader(lines, fileName);
            var f = new LaenderFile();
            f.Magic = r.ReadInt();
            while (!r.AtEnd)
            {
                var n = new Nation();
                n.Name = r.ReadString();
                n.Rating = r.ReadInt();
                n.Code = r.ReadString();
                n.Values = r.ReadInts(Nation.ValueCount);
                f.Nations.Add(n);
            }
            return f;
        }

        public override void WriteLines(List<string> o)
        {
            o.Add(SavText.Format(Magic));
            foreach (var n in Nations)
            {
                o.Add(n.Name);
                o.Add(SavText.Format(n.Rating));
                o.Add(n.Code);
                foreach (var v in n.Values) o.Add(SavText.Format(v));
            }
        }

        public override string Describe()
        {
            return string.Format("Laender: {0} nations", Nations.Count);
        }
    }

    /// <summary>
    /// LIGA*.SAV: the number of teams n, then n team slots, then the fixture table of
    /// 2*(n-1) matchdays x n entries (the opponent of each team on each matchday).
    /// </summary>
    public sealed class LigaFile : SavFile
    {
        public int TeamCount { get; set; }
        public int[] TeamIds { get; set; }
        /// <summary>All remaining numbers, matchday by matchday.</summary>
        public int[] Fixtures { get; set; }

        public static LigaFile Parse(IList<string> lines, string fileName)
        {
            var r = new LineReader(lines, fileName);
            var f = new LigaFile();
            f.TeamCount = r.ReadInt();
            if (f.TeamCount < 0 || f.TeamCount > r.Remaining) throw r.Error("invalid team count");
            f.TeamIds = r.ReadInts(f.TeamCount);
            f.Fixtures = r.ReadInts(r.Remaining);
            return f;
        }

        public override void WriteLines(List<string> o)
        {
            o.Add(SavText.Format(TeamCount));
            foreach (var v in TeamIds) o.Add(SavText.Format(v));
            foreach (var v in Fixtures) o.Add(SavText.Format(v));
        }

        /// <summary>True when the fixture table has the expected 2*(n-1)*n entries (double round robin).</summary>
        public bool HasDoubleRoundRobinShape
        {
            get { return Fixtures.Length == 2 * (TeamCount - 1) * TeamCount; }
        }

        public override string Describe()
        {
            return string.Format("Liga: {0} teams, {1} fixture entries{2}", TeamCount, Fixtures.Length,
                HasDoubleRoundRobinShape ? " (double round robin)" : " (unexpected shape)");
        }
    }

    /// <summary>Layout of a file made of identical fixed-size records: 'S' = text line, 'N' = integer line.</summary>
    public sealed class RecordSchema
    {
        public string Name { get; private set; }
        public string Pattern { get; private set; }
        public bool HasMagic { get; private set; }

        public RecordSchema(string name, string pattern, bool hasMagic)
        {
            Name = name;
            Pattern = pattern;
            HasMagic = hasMagic;
        }

        public int Stride { get { return Pattern.Length; } }

        // Trainers/referees/appearance: 2 names + 3 numbers. Assistants and managers: 2 names + 2 numbers.
        public static readonly RecordSchema Trainer = new RecordSchema("Trainer", "SSNNN", false);
        public static readonly RecordSchema AssistantTrainer = new RecordSchema("AssistantTrainer", "SSNN", false);
        public static readonly RecordSchema Manager = new RecordSchema("Manager", "SSNN", false);
        public static readonly RecordSchema Referee = new RecordSchema("Referee", "SSNNN", false);
        public static readonly RecordSchema Appearance = new RecordSchema("Appearance", "SSNNN", false);
        public static readonly RecordSchema Prominent = new RecordSchema("Prominent", "SSN", false);
        public static readonly RecordSchema Customer = new RecordSchema("Customer", "SSN", false);
        /// <summary>Sponsor: name, 15 numbers, font name, 2 numbers (19 lines)..</summary>
        public static readonly RecordSchema Sponsor = new RecordSchema("Sponsor", "S" + new string('N', 15) + "SNN", false);
        /// <summary>Stadium: name, city, 29 numbers (31 lines); one record per team of the matching Land file.</summary>
        public static readonly RecordSchema Stadium = new RecordSchema("Stadium", "SS" + new string('N', 29), false);
        /// <summary>Name pools (JUGE*, JUGEND*): magic, then first name / last name pairs.</summary>
        public static readonly RecordSchema NamePool = new RecordSchema("NamePool", "SS", true);
    }

    /// <summary>A file of identical fixed-size records. Values are kept as the original text tokens.</summary>
    public sealed class RecordFile : SavFile
    {
        public RecordSchema Schema { get; private set; }
        public int Magic { get; set; }
        public List<string[]> Records { get; private set; }

        public RecordFile(RecordSchema schema)
        {
            Schema = schema;
            Magic = SavConstants.Magic;
            Records = new List<string[]>();
        }

        public static RecordFile Parse(IList<string> lines, string fileName, RecordSchema schema)
        {
            var r = new LineReader(lines, fileName);
            var f = new RecordFile(schema);
            if (schema.HasMagic) f.Magic = r.ReadInt();
            if (r.Remaining % schema.Stride != 0)
                throw new FormatException(fileName + ": " + r.Remaining + " lines is not a multiple of the " +
                                          schema.Name + " record size of " + schema.Stride + ".");
            while (!r.AtEnd)
            {
                var rec = new string[schema.Stride];
                for (var i = 0; i < rec.Length; i++)
                {
                    if (schema.Pattern[i] == 'N')
                    {
                        // validate and keep canonical text
                        rec[i] = SavText.Format(r.ReadInt());
                    }
                    else
                    {
                        rec[i] = r.ReadString();
                    }
                }
                f.Records.Add(rec);
            }
            return f;
        }

        public int GetInt(int record, int field)
        {
            return int.Parse(Records[record][field], System.Globalization.CultureInfo.InvariantCulture);
        }

        public override void WriteLines(List<string> o)
        {
            if (Schema.HasMagic) o.Add(SavText.Format(Magic));
            foreach (var rec in Records) o.AddRange(rec);
        }

        public override string Describe()
        {
            return string.Format("{0}: {1} records", Schema.Name, Records.Count);
        }
    }

    /// <summary>Files kept as plain lines (KLEINIG, Fangesa, FanWaves, ExSpiel) until their fields are decoded.</summary>
    public sealed class RawFile : SavFile
    {
        public string Kind { get; private set; }
        public List<string> Lines { get; private set; }

        public RawFile(string kind, IEnumerable<string> lines)
        {
            Kind = kind;
            Lines = new List<string>(lines);
        }

        public override void WriteLines(List<string> o)
        {
            o.AddRange(Lines);
        }

        public override string Describe()
        {
            return string.Format("{0}: {1} lines (raw)", Kind, Lines.Count);
        }
    }

    /// <summary>Picks the right model for a file from its name and parses it.</summary>
    public static class SavLoader
    {
        public static SavFile FromBytes(string fileName, byte[] data)
        {
            var lines = SavText.ToLines(data, fileName);
            var name = System.IO.Path.GetFileName(fileName).ToUpperInvariant();

            if (name == "AVEREINE.SAV") return AvereineFile.Parse(lines, fileName);
            if (name == "LAENDER.SAV") return LaenderFile.Parse(lines, fileName);
            if (name.StartsWith("LAND", StringComparison.Ordinal)) return LandFile.Parse(lines, fileName);
            if (name.StartsWith("LIGA", StringComparison.Ordinal)) return LigaFile.Parse(lines, fileName);
            if (name.StartsWith("STAD", StringComparison.Ordinal)) return RecordFile.Parse(lines, fileName, RecordSchema.Stadium);
            if (name.StartsWith("SPON", StringComparison.Ordinal)) return RecordFile.Parse(lines, fileName, RecordSchema.Sponsor);
            if (name.StartsWith("TRAI", StringComparison.Ordinal)) return RecordFile.Parse(lines, fileName, RecordSchema.Trainer);
            if (name.StartsWith("TTRA", StringComparison.Ordinal)) return RecordFile.Parse(lines, fileName, RecordSchema.AssistantTrainer);
            if (name.StartsWith("MANA", StringComparison.Ordinal)) return RecordFile.Parse(lines, fileName, RecordSchema.Manager);
            if (name.StartsWith("NSCH", StringComparison.Ordinal)) return RecordFile.Parse(lines, fileName, RecordSchema.Referee);
            if (name.StartsWith("PROM", StringComparison.Ordinal)) return RecordFile.Parse(lines, fileName, RecordSchema.Prominent);
            if (name == "ISCHIRIS.SAV") return RecordFile.Parse(lines, fileName, RecordSchema.Appearance);
            if (name == "KUNDEN.SAV") return RecordFile.Parse(lines, fileName, RecordSchema.Customer);
            if (Regex.IsMatch(name, @"^JUGE(ND\d+|DEUT|ENGL|FRAN|ITAL|SPAN)\.SAV$"))
                return RecordFile.Parse(lines, fileName, RecordSchema.NamePool);

            return new RawFile(System.IO.Path.GetFileNameWithoutExtension(fileName), lines);
        }

        public static SavFile Load(string path)
        {
            return FromBytes(path, System.IO.File.ReadAllBytes(path));
        }
    }
}
