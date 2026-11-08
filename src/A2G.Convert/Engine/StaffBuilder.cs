using System;
using System.Collections.Generic;
using System.Linq;
using A2G.Convert.Fm;
using A2G.Sav;

namespace A2G.Convert.Engine
{
    /// <summary>
    /// Turns FM staff into the people of Anstoss 2: club trainers, the trainer / assistant / manager / referee pools of a
    /// country and the prominent people. FM26 saves do not label a person's job, so the best-rated staff member of a club
    /// is used as its trainer and the pools are filled with the best-rated staff of the nation.
    /// Competence values are scaled from FM Current Ability (staff CA 100 is average, 190 the best coaches).
    /// </summary>
    public static class StaffBuilder
    {
        /// <summary>Linear scale of a Current Ability range onto a value range, clamped.</summary>
        public static int Scale(int ability, int abilityLow, int abilityHigh, int low, int high)
        {
            var t = (ability - abilityLow) / (double)(abilityHigh - abilityLow);
            return Math.Max(low, Math.Min(high, (int)Math.Round(low + t * (high - low))));
        }

        /// <summary>Trainer type 0..5 (buddy, funny guy, motivator, PR man, driller, scientist) from coaching preferences.</summary>
        public static int TrainerType(FmStaff s)
        {
            if (s.TrainingHardness >= 15) return 4;                 // driller
            var spread = new[] { 0, 1, 2, 3, 5 };                   // the other types, spread deterministically
            return spread[Math.Abs(Hash(s.Uid)) % spread.Length];
        }

        public static void Names(FmStaff s, out string first, out string last)
        {
            var l = string.IsNullOrWhiteSpace(s.LastName) ? s.DisplayName : s.LastName;
            var f = s.FirstName;
            if (string.IsNullOrWhiteSpace(f))
            {
                var parts = (s.DisplayName ?? string.Empty).Split(' ');
                f = parts.Length > 1 ? parts[0] : "-";
                if (parts.Length > 1) l = string.Join(" ", parts.Skip(1));
            }
            first = NameShortener.Fit(f, NameShortener.MaxFirstName);
            last = NameShortener.Fit(l, NameShortener.MaxLastName);
            if (first.Length == 0) first = "-";
            if (last.Length == 0) last = "-";
        }

        private static int Age(FmStaff s) { return Math.Max(20, Math.Min(90, s.Age)); }

        /// <summary>The trainer block of a team in a Land file: surname, first name, strength, type, age.</summary>
        public static PersonRecord TrainerBlock(FmStaff s)
        {
            string first, last;
            Names(s, out first, out last);
            return new PersonRecord { Name1 = last, Name2 = first, Values = new[] { Scale(s.CurrentAbility, 100, 180, 2, 14), TrainerType(s), Age(s) } };
        }

        // The pool files list the first name before the surname.
        public static string[] TrainerRecord(FmStaff s)
        {
            string first, last; Names(s, out first, out last);
            return new[] { first, last, I(Scale(s.CurrentAbility, 100, 180, 2, 14)), I(TrainerType(s)), I(Age(s)) };
        }

        public static string[] AssistantRecord(FmStaff s)
        {
            string first, last; Names(s, out first, out last);
            return new[] { first, last, I(Scale(s.CurrentAbility, 100, 170, 5, 14)), I(Age(s)) };
        }

        public static string[] ManagerRecord(FmStaff s)
        {
            string first, last; Names(s, out first, out last);
            return new[] { first, last, I(Scale(s.CurrentAbility, 100, 190, 1, 15)), I(Age(s)) };
        }

        public static string[] RefereeRecord(FmStaff s)
        {
            string first, last; Names(s, out first, out last);
            var strictness = s.TrainingHardness > 0 ? s.TrainingHardness : s.Temperament;
            return new[] { first, last, I(Scale(s.CurrentAbility, 90, 160, 1, 10)), I(Math.Max(0, Math.Min(10, (int)Math.Round(strictness / 2.0)))), "0" };
        }

        /// <summary>A prominent person: surname, first name and the index of the favourite club (1-based, within the country).</summary>
        public static string[] ProminentRecord(FmStaff s, int clubCount)
        {
            string first, last; Names(s, out first, out last);
            return new[] { last, first, I(1 + Math.Abs(Hash(s.Uid)) % Math.Max(1, clubCount)) };
        }

        private static string I(int v) { return SavText.Format(v); }

        private static int Hash(string text)
        {
            unchecked
            {
                var h = 23;
                foreach (var c in text ?? string.Empty) h = h * 31 + c;
                return h & 0x7FFFFFFF;
            }
        }
    }

    /// <summary>Hands out staff of one country without using a person twice.</summary>
    public sealed class StaffPlanner
    {
        private readonly FmDataSet _fm;
        private readonly int _nationId;
        private readonly HashSet<string> _used = new HashSet<string>();

        public StaffPlanner(FmDataSet fm, int nationId)
        {
            _fm = fm;
            _nationId = nationId;
        }

        /// <summary>The best-rated staff member of the club who is not used yet, as a trainer block; the template if there is none.</summary>
        public PersonRecord TrainerFor(FmClub club, PersonRecord template)
        {
            var candidate = _fm.StaffOf(club).FirstOrDefault(s => s.Age >= 30 && !_used.Contains(s.Uid));
            if (candidate == null) candidate = _fm.StaffOf(club).FirstOrDefault(s => !_used.Contains(s.Uid));
            if (candidate == null)
                return new PersonRecord { Name1 = template.Name1, Name2 = template.Name2, Values = (int[])template.Values.Clone() };
            _used.Add(candidate.Uid);
            return StaffBuilder.TrainerBlock(candidate);
        }

        /// <summary>The next best unused staff members of the nation (adult coaching age).</summary>
        public List<FmStaff> Take(int count)
        {
            var result = _fm.StaffOfNation(_nationId).Where(s => s.Age >= 28 && !_used.Contains(s.Uid)).Take(count).ToList();
            foreach (var s in result) _used.Add(s.Uid);
            return result;
        }
    }
}
