using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using A2G.Convert.Engine;
using A2G.Convert.Plan;
using A2G.Sav;

namespace A2G.Convert.Writing
{
    /// <summary>
    /// Checks a written data folder against the original install: same files, same record counts, and every value
    /// inside the limits the game documents (names, squad sizes, strength 1-7, ages, positions, nationality indexes,
    /// league team ids). It reads the files from disk, independently of the writer.
    /// </summary>
    public static class SavValidator
    {
        private static readonly string[] GeneratedPools = { "JUGEDEUT.SAV", "JUGEENGL.SAV", "JUGEFRAN.SAV", "JUGEITAL.SAV", "JUGESPAN.SAV" };

        public static List<string> Validate(string dataFolder, string originalFolder)
        {
            var errors = new List<string>();
            var originalFiles = Directory.GetFiles(originalFolder).Select(Path.GetFileName).ToList();
            foreach (var name in originalFiles)
                if (!File.Exists(Path.Combine(dataFolder, name))) errors.Add("missing file: " + name);

            LaenderFile laender = null;
            try { laender = (LaenderFile)SavLoader.Load(Path.Combine(dataFolder, "LAENDER.SAV")); }
            catch (Exception ex) { errors.Add("LAENDER.SAV: " + ex.Message); }
            var nationCount = laender != null ? laender.Nations.Count : 76;

            foreach (var spec in A2Structure.Countries())
            {
                try { ValidateCountry(spec, dataFolder, originalFolder, nationCount, errors); }
                catch (Exception ex) { errors.Add(spec.Key + ": " + ex.Message); }
            }
            try { ValidateAvereine(dataFolder, originalFolder, nationCount, errors); }
            catch (Exception ex) { errors.Add("Avereine.sav: " + ex.Message); }

            foreach (var name in originalFiles)
            {
                try { ValidateStaffFile(name, dataFolder, originalFolder, errors); }
                catch (Exception ex) { errors.Add(name + ": " + ex.Message); }
            }

            foreach (var name in originalFiles)
            {
                // all name pools (national and youth) are generated from FM players
                if (!name.StartsWith("JUGE", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    var a = (RecordFile)SavLoader.Load(Path.Combine(originalFolder, name));
                    var b = (RecordFile)SavLoader.Load(Path.Combine(dataFolder, name));
                    if (a.Records.Count != b.Records.Count) errors.Add(name + ": record count " + b.Records.Count + " instead of " + a.Records.Count);
                    foreach (var r in b.Records)
                        if (r[0].Length < 1 || r[0].Length > NameShortener.MaxFirstName || r[1].Length < 1 || r[1].Length > NameShortener.MaxLastName)
                        { errors.Add(name + ": name out of limits: " + r[0] + " " + r[1]); break; }
                }
                catch (Exception ex) { errors.Add(name + ": " + ex.Message); }
            }
            return errors;
        }

        /// <summary>Trainer, assistant, manager, referee and prominent files: same counts, name limits and value ranges.</summary>
        private static void ValidateStaffFile(string name, string dir, string orig, List<string> errors)
        {
            var kinds = new[] { "TRAI", "TTRA", "MANA", "NSCH", "PROM" };
            var upper = name.ToUpperInvariant();
            if (!kinds.Any(k => upper.StartsWith(k, StringComparison.Ordinal)) || !upper.EndsWith(".SAV", StringComparison.Ordinal)) return;
            var a = (RecordFile)SavLoader.Load(Path.Combine(orig, name));
            var b = (RecordFile)SavLoader.Load(Path.Combine(dir, name));
            if (a.Records.Count != b.Records.Count) { errors.Add(name + ": record count " + b.Records.Count + " instead of " + a.Records.Count); return; }
            var prominent = upper.StartsWith("PROM", StringComparison.Ordinal);
            var prefix = upper.Substring(0, 4);
            for (var i = 0; i < b.Records.Count; i++)
            {
                var r = b.Records[i];
                // Land trainer blocks list the surname first, the pool files the first name first; the limits are the same
                var n1 = r[0].Length; var n2 = r[1].Length;
                var firstLen = prominent ? n2 : n1; var lastLen = prominent ? n1 : n2;
                if (firstLen < 1 || firstLen > NameShortener.MaxFirstName || lastLen < 1 || lastLen > NameShortener.MaxLastName)
                    errors.Add(name + " record " + (i + 1) + ": name out of limits");
                var ints = r.Skip(2).Select(x => int.Parse(x)).ToArray();
                if (prefix == "TRAI" && (ints[0] < 1 || ints[0] > 15 || ints[1] < 0 || ints[1] > 6 || ints[2] < 20 || ints[2] > 90)) errors.Add(name + " record " + (i + 1) + ": value out of range");
                if (prefix == "TTRA" && (ints[0] < 1 || ints[0] > 15 || ints[1] < 20 || ints[1] > 90)) errors.Add(name + " record " + (i + 1) + ": value out of range");
                if (prefix == "MANA" && (ints[0] < 1 || ints[0] > 15 || ints[1] < 20 || ints[1] > 90)) errors.Add(name + " record " + (i + 1) + ": value out of range");
                if (prefix == "NSCH" && (ints[0] < 0 || ints[0] > 10 || ints[1] < 0 || ints[1] > 10)) errors.Add(name + " record " + (i + 1) + ": value out of range");
            }
        }

        private static void ValidateCountry(A2CountrySpec spec, string dir, string orig, int nationCount, List<string> errors)
        {
            var land = (LandFile)SavLoader.Load(Path.Combine(dir, spec.LandFile));
            var landOrig = (LandFile)SavLoader.Load(Path.Combine(orig, spec.LandFile));
            var stad = (RecordFile)SavLoader.Load(Path.Combine(dir, spec.StadFile));
            var stadOrig = (RecordFile)SavLoader.Load(Path.Combine(orig, spec.StadFile));

            if (land.Teams.Count != landOrig.Teams.Count) errors.Add(spec.LandFile + ": " + land.Teams.Count + " teams instead of " + landOrig.Teams.Count);
            if (stad.Records.Count != stadOrig.Records.Count || stad.Records.Count != land.Teams.Count)
                errors.Add(spec.StadFile + ": " + stad.Records.Count + " stadiums for " + land.Teams.Count + " teams");
            if (land.TailClubNames.Count != landOrig.TailClubNames.Count) errors.Add(spec.LandFile + ": tail name count changed");
            if (land.LeagueCount != landOrig.LeagueCount) errors.Add(spec.LandFile + ": league count changed");

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < land.Teams.Count; i++)
            {
                var t = land.Teams[i];
                var label = spec.LandFile + " team " + (i + 1) + " (" + t.Name + ")";
                if (t.Name.Length < 1 || t.Name.Length > ClubNamer.MaxName) errors.Add(label + ": name length");
                if (t.City.Length < 1 || t.City.Length > ClubNamer.MaxCity) errors.Add(label + ": city length");
                if (t.Nickname.Length < 1 || t.Nickname.Length > ClubNamer.MaxNickname) errors.Add(label + ": nickname length");
                if (!names.Add(t.Name)) errors.Add(label + ": duplicate team name");
                ValidatePlayers(label, t.Players, 16, 30, 2, nationCount, errors);
                var v = t.StaffBlock.Values;
                if (!KitOk(v[2], v[3], v[4]) || !KitOk(v[5], v[6], v[7])) errors.Add(label + ": kit out of range (shirt = pattern*16 + colour with pattern 0-6 and colour 0-12; shorts and socks 0-12)");
                if (t.StaffBlock.Values[LandTeam.StaffBlockValueCount - 1] != t.Players.Count)
                    errors.Add(label + ": the staff block says " + t.StaffBlock.Values[LandTeam.StaffBlockValueCount - 1] + " players but the team has " + t.Players.Count + " (the game would read the wrong number of records)");
                if (t.Trainer.Name1.Length < 1 || t.Trainer.Name1.Length > NameShortener.MaxLastName ||
                    t.Trainer.Name2.Length < 1 || t.Trainer.Name2.Length > NameShortener.MaxFirstName) errors.Add(label + ": trainer name out of limits");
                var stadium = stad.Records[i];
                if (stadium[0].Length < 3 || stadium[0].Length > 28) errors.Add(label + ": stadium name length");
            }

            foreach (var tier in spec.Tiers)
                foreach (var l in tier.Leagues)
                {
                    var liga = (LigaFile)SavLoader.Load(Path.Combine(dir, l.LigaFile));
                    var ligaOrig = (LigaFile)SavLoader.Load(Path.Combine(orig, l.LigaFile));
                    if (liga.TeamCount != ligaOrig.TeamCount || liga.Fixtures.Length != ligaOrig.Fixtures.Length)
                        errors.Add(l.LigaFile + ": shape changed");
                    if (liga.TeamIds.Distinct().Count() != liga.TeamIds.Length) errors.Add(l.LigaFile + ": duplicate team ids");
                    if (liga.TeamIds.Any(id => id < 1 || id > land.Teams.Count)) errors.Add(l.LigaFile + ": team id out of range");
                }
        }

        private static void ValidateAvereine(string dir, string orig, int nationCount, List<string> errors)
        {
            var file = (AvereineFile)SavLoader.Load(Path.Combine(dir, "Avereine.sav"));
            var o = (AvereineFile)SavLoader.Load(Path.Combine(orig, "Avereine.sav"));
            if (file.Teams.Count != o.Teams.Count) errors.Add("Avereine.sav: " + file.Teams.Count + " clubs instead of " + o.Teams.Count);
            if (file.ExtraClubs.Count != o.ExtraClubs.Count) errors.Add("Avereine.sav: spare club count changed");
            // the game groups clubs by nation: the nation of every slot must be the one of the shipped file (sorted, 30 + 32 nations)
            if (file.Teams.Count == o.Teams.Count && !file.Teams.Select(t => t.Country).SequenceEqual(o.Teams.Select(t => t.Country)))
                errors.Add("Avereine.sav: the clubs are not in the nation layout of the shipped file (the game aborts while loading)");
            if (file.ExtraClubs.Count == o.ExtraClubs.Count && !file.ExtraClubs.Select(t => t.Country).SequenceEqual(o.ExtraClubs.Select(t => t.Country)))
                errors.Add("Avereine.sav: the spare clubs are not in the nation layout of the shipped file");
            if (file.CountryTriplets.Count != o.CountryTriplets.Count) errors.Add("Avereine.sav: triplet count changed");
            for (var i = 0; i < file.Teams.Count; i++)
            {
                var t = file.Teams[i];
                var label = "Avereine club " + (i + 1) + " (" + t.Name + ")";
                if (t.Name.Length < 1 || t.Name.Length > ClubNamer.MaxName) errors.Add(label + ": name length");
                if (t.ShortName.Length < 1 || t.ShortName.Length > ClubNamer.MaxAvereineShortName) errors.Add(label + ": short name length");
                if (!KitOk(t.HomeShirt, t.HomeShorts, t.HomeSocks) || !KitOk(t.AwayShirt, t.AwayShorts, t.AwaySocks)) errors.Add(label + ": kit out of range");
                if (t.Country < 0 || t.Country >= nationCount) errors.Add(label + ": country index");
                if (t.Players.Count != o.Teams[i].Players.Count) errors.Add(label + ": " + t.Players.Count + " players instead of " + o.Teams[i].Players.Count);
                ValidatePlayers(label, t.Players, 1, 30, 1, nationCount, errors);
            }
        }

        /// <summary>A kit set: shirt = pattern * 16 + colour (pattern 0-6, colour 0-12), shorts and socks are colours 0-12.</summary>
        private static bool KitOk(int shirt, int shorts, int socks)
        {
            return shirt >= 0 && shirt / 16 <= 6 && shirt % 16 <= 12 && shorts >= 0 && shorts <= 12 && socks >= 0 && socks <= 12;
        }

        private static void ValidatePlayers(string label, List<Player> players, int min, int max, int minKeepers, int nationCount, List<string> errors)
        {
            if (players.Count < min || players.Count > max) errors.Add(label + ": " + players.Count + " players");
            if (players.Count(p => p.Position1 == 1) < minKeepers) errors.Add(label + ": fewer than " + minKeepers + " goalkeeper(s)");
            foreach (var p in players)
            {
                var who = label + " / " + p.FirstName + " " + p.LastName;
                if (p.LastName.Length < 1 || p.LastName.Length > NameShortener.MaxLastName) errors.Add(who + ": last name length");
                if (p.FirstName.Length < 1 || p.FirstName.Length > NameShortener.MaxFirstName) errors.Add(who + ": first name length");
                if (p.Strength < 1 || p.Strength > 7) errors.Add(who + ": strength " + p.Strength);
                if (p.Age < 17 || p.Age > 50) errors.Add(who + ": age " + p.Age);
                if (p.Position1 < 1 || p.Position1 > 10 || p.Position2 < 0 || p.Position2 > 10 || p.Position3 < 0 || p.Position3 > 10)
                    errors.Add(who + ": position");
                if (p.Nationality < 0 || p.Nationality >= nationCount) errors.Add(who + ": nationality");
                if (p.PositiveSkills >= 4096 || p.NegativeSkills >= 4096) errors.Add(who + ": ability mask");
                if (BitCount(p.PositiveSkills) > 2) errors.Add(who + ": more than two positive abilities");
                if (p.Skin < 0 || p.Skin > 2) errors.Add(who + ": skin");
            }
        }

        private static int BitCount(int v)
        {
            var c = 0;
            while (v != 0) { c += v & 1; v >>= 1; }
            return c;
        }
    }
}
