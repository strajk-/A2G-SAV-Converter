using System;
using System.Collections.Generic;
using System.IO;

namespace A2G.Sav
{
    /// <summary>Magic number found as the first line of many Anstoss 2 files.</summary>
    public static class SavConstants
    {
        public const int Magic = 842610482;
    }

    /// <summary>Base of every typed .sav model. Parse with <see cref="SavLoader"/>, write with <see cref="ToBytes"/>.</summary>
    public abstract class SavFile
    {
        /// <summary>Serialises the model to its lines (without terminators).</summary>
        public abstract void WriteLines(List<string> output);

        /// <summary>A one-line human readable summary, used by tools and logs.</summary>
        public abstract string Describe();

        public byte[] ToBytes()
        {
            var lines = new List<string>();
            WriteLines(lines);
            return SavText.ToBytes(lines);
        }

        public void Save(string path)
        {
            File.WriteAllBytes(path, ToBytes());
        }
    }

    /// <summary>One person line group: two name lines followed by a fixed number of integers.</summary>
    public sealed class PersonRecord
    {
        public string Name1 { get; set; }
        public string Name2 { get; set; }
        public int[] Values { get; set; }

        internal static PersonRecord Read(LineReader r, int valueCount)
        {
            var p = new PersonRecord();
            p.Name1 = r.ReadString();
            p.Name2 = r.ReadString();
            p.Values = r.ReadInts(valueCount);
            return p;
        }

        internal void Write(List<string> o)
        {
            o.Add(Name1);
            o.Add(Name2);
            foreach (var v in Values) o.Add(SavText.Format(v));
        }
    }

    /// <summary>
    /// A player as stored in Land*.sav and Avereine.sav: 2 name lines + 13 integers (15 lines).
    /// Field meanings are derived from the game's editor and from value ranges in the original data (see docs\Dev\sav-format-verified.md).
    /// </summary>
    public sealed class Player
    {
        public const int LineCount = 15;

        public string LastName { get; set; }
        public string FirstName { get; set; }

        /// <summary>Always 0 in the original data except for a couple of stray values; meaning unknown.</summary>
        public int Unknown0 { get; set; }
        /// <summary>0 = white, 1 = dark, 2 = black.</summary>
        public int Skin { get; set; }
        /// <summary>Hair colour/style index (0..5 observed in team files).</summary>
        public int Hair { get; set; }
        public int Age { get; set; }
        /// <summary>Overall strength; 1 (poor) .. 7 (world class) in team files.</summary>
        public int Strength { get; set; }
        /// <summary>Index into LAENDER.SAV (alphabetical, 0 = "Sonstige").</summary>
        public int Nationality { get; set; }
        /// <summary>Position codes; 0 = none for the secondary slots. See docs\Dev\sav-format-verified.md.</summary>
        public int Position1 { get; set; }
        public int Position2 { get; set; }
        public int Position3 { get; set; }
        /// <summary>Bitmask of positive skills.</summary>
        public int PositiveSkills { get; set; }
        /// <summary>Bitmask of negative skills.</summary>
        public int NegativeSkills { get; set; }
        /// <summary>Bitmask of traits.</summary>
        public int Traits { get; set; }
        /// <summary>Bitmask of character and health flags.</summary>
        public int CharacterHealth { get; set; }

        internal static Player Read(LineReader r)
        {
            var p = new Player();
            p.LastName = r.ReadString();
            p.FirstName = r.ReadString();
            p.Unknown0 = r.ReadInt();
            p.Skin = r.ReadInt();
            p.Hair = r.ReadInt();
            p.Age = r.ReadInt();
            p.Strength = r.ReadInt();
            p.Nationality = r.ReadInt();
            p.Position1 = r.ReadInt();
            p.Position2 = r.ReadInt();
            p.Position3 = r.ReadInt();
            p.PositiveSkills = r.ReadInt();
            p.NegativeSkills = r.ReadInt();
            p.Traits = r.ReadInt();
            p.CharacterHealth = r.ReadInt();
            return p;
        }

        /// <summary>Whether the lines at the reader's position look like a player record.</summary>
        internal static bool LooksLikeAt(LineReader r)
        {
            if (r.Remaining < LineCount) return false;
            if (!r.StringAhead(0) || !r.StringAhead(1)) return false;
            for (var i = 2; i < LineCount; i++) if (!r.IntAhead(i)) return false;
            return true;
        }

        internal void Write(List<string> o)
        {
            o.Add(LastName);
            o.Add(FirstName);
            o.Add(SavText.Format(Unknown0));
            o.Add(SavText.Format(Skin));
            o.Add(SavText.Format(Hair));
            o.Add(SavText.Format(Age));
            o.Add(SavText.Format(Strength));
            o.Add(SavText.Format(Nationality));
            o.Add(SavText.Format(Position1));
            o.Add(SavText.Format(Position2));
            o.Add(SavText.Format(Position3));
            o.Add(SavText.Format(PositiveSkills));
            o.Add(SavText.Format(NegativeSkills));
            o.Add(SavText.Format(Traits));
            o.Add(SavText.Format(CharacterHealth));
        }
    }
}
