using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using A2G.Convert.Engine;
using A2G.Convert.Fm;
using A2G.Convert.Plan;
using A2G.Sav;

namespace A2G.Convert.Writing
{
    /// <summary>What a write run did, for the report and the UI.</summary>
    public sealed class WriteReport
    {
        public WriteReport()
        {
            Notes = new List<string>();
            Warnings = new List<string>();
            FilesWritten = new List<string>();
        }
        public List<string> Notes { get; private set; }
        public List<string> Warnings { get; private set; }
        public List<string> FilesWritten { get; private set; }
    }

    /// <summary>
    /// Writes a <see cref="ConversionPlan"/> as Anstoss 2 .sav files. The output folder mirrors the install
    /// (<c>DATA.DEF</c> and <c>DATA.VER</c>): both are first seeded with a copy of the original folders so that every file
    /// the converter does not generate is still present, then the generated files replace the originals. Every file keeps
    /// the record counts of the original, because the game expects them.
    /// </summary>
    public static class SavWriter
    {
        // Index of the stadium size rating in a Stad record (29 numbers); used to pair big stadiums with big clubs.

        public static WriteReport Write(ConversionPlan plan, FmDataSet fm, ConversionSettings settings,
            string a2InstallDir, string outputAnstoss2Dir, string crestFolder = null, int fmYear = 0)
        {
            var report = new WriteReport();
            var templateDir = Path.Combine(a2InstallDir, "DATA.DEF");
            if (!Directory.Exists(templateDir))
                throw new DirectoryNotFoundException("DATA.DEF not found in the Anstoss 2 folder: " + templateDir);

            var outDef = Path.Combine(outputAnstoss2Dir, "DATA.DEF");
            var outVer = Path.Combine(outputAnstoss2Dir, "DATA.VER");
            CopyFolder(templateDir, outDef);
            var verSource = Path.Combine(a2InstallDir, "DATA.VER");
            CopyFolder(Directory.Exists(verSource) ? verSource : templateDir, outVer);

            var laender = (LaenderFile)SavLoader.Load(Path.Combine(templateDir, "LAENDER.SAV"));
            var nations = new A2Nations(laender.Nations.Select(n => n.Name).ToList());
            var mapper = new PlayerMapper(settings, fm, nations);

            var generated = new Dictionary<string, SavFile>(StringComparer.OrdinalIgnoreCase);

            _library = new CrestLibrary(crestFolder);
            var crests = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);   // file name in WAPPEN -> BMP
            var crestRows = new List<string[]>();                  // country, file, written name, FM name, league
            foreach (var country in plan.Countries)
                WriteCountry(country, templateDir, mapper, fm, generated, report, crests, crestRows);

            generated["Avereine.sav"] = BuildAvereine(plan.Avereine, templateDir, fm, mapper, nations, report);

            AddNamePool("JUGEDEUT.SAV", new[] { "Germany" }, templateDir, fm, generated, report);
            AddNamePool("JUGEENGL.SAV", new[] { "England" }, templateDir, fm, generated, report);
            AddNamePool("JUGEFRAN.SAV", new[] { "France" }, templateDir, fm, generated, report);
            AddNamePool("JUGEITAL.SAV", new[] { "Italy" }, templateDir, fm, generated, report);
            AddNamePool("JUGESPAN.SAV", new[] { "Spain" }, templateDir, fm, generated, report);
            for (var i = 0; i < YouthPools.Length; i++)
                AddNamePool("JUGEND" + (i + 1) + ".SAV", YouthPools[i].SelectMany(A2Nations.FmNamesOf), templateDir, fm, generated, report);

            foreach (var kv in generated)
            {
                kv.Value.Save(Path.Combine(outDef, kv.Key));
                kv.Value.Save(Path.Combine(outVer, kv.Key));
                report.FilesWritten.Add(kv.Key);
            }

            foreach (var crest in crests)
                foreach (var target in new[] { outDef, outVer })
                {
                    var folder = Path.Combine(target, "WAPPEN");
                    Directory.CreateDirectory(folder);
                    File.WriteAllBytes(CrestWriter.FilePath(folder, crest.Key.Substring(3, 1), int.Parse(crest.Key.Substring(4, crest.Key.IndexOf('.') - 4))), crest.Value);
                }
            var startYear = settings.StartYear > 0 ? settings.StartYear : fmYear;
            if (startYear > 0)
                foreach (var target in new[] { outDef, outVer })
                    if (!GameYear.Apply(Path.Combine(target, "KLEINIG.SAV"), startYear, report)) break;
            if (crests.Count > 0)
                foreach (var target in new[] { outDef, outVer })
                    File.WriteAllText(Path.Combine(target, "WAPPEN", "Crests.md"), CrestWriter.Overview(crestRows), new System.Text.UTF8Encoding(true));
            if (crests.Count > 0)
                report.Notes.Add("Crests: " + crests.Count + " WAPPEN\\VW_*.BMP files: " + crestRows.Count(r => r[5].StartsWith("your crest")) +
                                 " from your crest folder, " + crestRows.Count(r => r[5] == "generated") + " generated (letters and colours per club), the rest original; see WAPPEN\\Crests.md.");

            report.Notes.Add("Not generated from FM data (kept from the original files): sponsors, ISCHIRIS, Kunden, KLEINIG, Fangesa, " +
                             "the 21-number staff block and the stadium numbers of each team, the tail person of the Land files and the " +
                             "Avereine country triplets.");
            report.Warnings.AddRange(plan.Warnings);
            return report;
        }

        // ---- countries ----------------------------------------------------------------------

        private sealed class Slot
        {
            public LandTeam Team;
            public string[] Stadium;
            public FmClub Club;          // null: the slot keeps the original team
            public int TemplateIndex;
            public string Group;         // league / group the slot belongs to    // 0-based position of the original team in the Land file
        }

        private static void WriteCountry(CountryPlan country, string templateDir, PlayerMapper mapper, FmDataSet fm,
            Dictionary<string, SavFile> generated, WriteReport report, Dictionary<string, byte[]> crests, List<string[]> crestRows)
        {
            var spec = country.Spec;
            var nation = fm.NationByName(spec.FmNationName);
            var planner = nation != null && fm.Staff.Count > 0 ? new StaffPlanner(fm, nation.SaveId) : null;
            if (planner == null) report.Warnings.Add(spec.Key + ": no FM staff data (staff.csv); trainers and staff files keep the original data.");
            var land = (LandFile)SavLoader.Load(Path.Combine(templateDir, spec.LandFile));
            var stad = (RecordFile)SavLoader.Load(Path.Combine(templateDir, spec.StadFile));

            // template teams grouped by league (via the original LIGA files) and the spare teams
            var templateByLeague = new List<List<int>>();
            var inLeague = new HashSet<int>();
            var ligas = new List<LigaFile>();
            foreach (var tier in spec.Tiers)
                foreach (var l in tier.Leagues)
                {
                    var liga = (LigaFile)SavLoader.Load(Path.Combine(templateDir, l.LigaFile));
                    ligas.Add(liga);
                    var idx = liga.TeamIds.Select(id => id - 1).ToList();
                    templateByLeague.Add(idx);
                    foreach (var i in idx) inLeague.Add(i);
                }
            var templateSpares = Enumerable.Range(0, land.Teams.Count).Where(i => !inLeague.Contains(i)).ToList();

            var newOrder = new List<Slot>();
            var newLigaIds = new List<List<int>>();
            var leagueNo = 0;
            var tierNo = 0;
            foreach (var tier in country.Tiers)
            {
                foreach (var league in tier.Leagues)
                {
                    var slots = BuildSlots(league.Teams, templateByLeague[leagueNo], land, stad, mapper, planner, report,
                        spec.Key + " " + league.Spec.Name);
                    SetSeasonResults(slots, league.Teams, SeasonResults.LeagueKey(league.Spec), tierNo == 0, templateByLeague[leagueNo].Select(i => land.Teams[i].StaffBlock.Values[SeasonResults.FlagIndex]), mapper.Settings);
                    var ids = new List<int>();
                    foreach (var s in slots) { newOrder.Add(s); ids.Add(newOrder.Count); }
                    newLigaIds.Add(ids);
                    leagueNo++;
                }
                tierNo++;
            }
            var spareSlots = BuildSlots(country.Spares, templateSpares, land, stad, mapper, planner, report, spec.Key + " spare teams");
            foreach (var s in spareSlots.Where(x => x.Club != null))   // spare clubs are in no league: no result of the last season
                s.Team.StaffBlock.Values[SeasonResults.FlagIndex] = SeasonResults.Encode(s.Team.StaffBlock.Values[SeasonResults.FlagIndex], new SeasonResult());
            foreach (var s in spareSlots)
                newOrder.Add(s);

            if (newOrder.Count != land.Teams.Count)
                throw new InvalidOperationException(spec.Key + ": team count changed from " + land.Teams.Count + " to " + newOrder.Count);

            CollectCrests(spec.Key, newOrder, templateDir, crests, crestRows, mapper.Settings);
            land.Teams.Clear();
            land.Teams.AddRange(newOrder.Select(s => s.Team));
            stad.Records.Clear();
            foreach (var s in newOrder) stad.Records.Add(s.Stadium);

            var tail = country.TailClubs.Select(ClubNamer.ClubName).ToList();
            for (var i = 0; i < land.TailClubNames.Count; i++)
            {
                if (i < tail.Count) land.TailClubNames[i] = tail[i];
                else report.Warnings.Add(spec.Key + ": spare club name " + (i + 1) + " kept from the original data.");
            }

            generated[spec.LandFile] = land;
            generated[spec.StadFile] = stad;
            if (planner != null) AddStaffFiles(spec, templateDir, planner, newOrder.Count, generated, report);

            leagueNo = 0;
            foreach (var tier in spec.Tiers)
                foreach (var l in tier.Leagues)
                {
                    var liga = ligas[leagueNo];
                    liga.TeamIds = newLigaIds[leagueNo].ToArray();   // fixtures refer to league slots and stay valid
                    generated[l.LigaFile] = liga;
                    leagueNo++;
                }
        }

        /// <summary>Writes the results of the last season (champion, runner-up, UEFA places, cup, promoted, relegated) into the staff blocks of a league's slots.</summary>
        private static void SetSeasonResults(List<Slot> slots, List<TeamPlan> teams, string groupKey, bool topDivision, IEnumerable<int> templateValues, ConversionSettings settings)
        {
            var results = SeasonResults.Effective(settings, groupKey, teams.Select(t => t.Club).ToList(), topDivision, SeasonResults.CountsFrom(templateValues));
            foreach (var s in slots.Where(x => x.Club != null))
            {
                SeasonResult r;
                results.TryGetValue(s.Club, out r);   // a club in no slot gets no result (the template's value must not stay)
                var values = s.Team.StaffBlock.Values;
                values[SeasonResults.FlagIndex] = SeasonResults.Encode(values[SeasonResults.FlagIndex], r);
            }
        }

        private static CrestLibrary _library;

        private static string CrestLetter(string countryKey) { return A2Structure.CrestLetter(countryKey); }

        /// <summary>
        /// The game shows WAPPEN\VW_x{n}.BMP for the n-th team of the country file. A generated crest per club; a slot that keeps its
        /// original team takes that team's original crest along to the new position.
        /// </summary>
        private static void CollectCrests(string countryKey, List<Slot> order, string templateDir, Dictionary<string, byte[]> crests, List<string[]> crestRows, ConversionSettings settings)
        {
            var letter = CrestLetter(countryKey);
            var folder = Path.Combine(templateDir, "WAPPEN");
            if (letter == null || !Directory.Exists(folder)) return;
            for (var i = 0; i < order.Count; i++)
            {
                var slot = order[i];
                byte[] bytes = null;
                string source = null;
                if (slot.Club != null)
                {
                    bytes = _library != null ? _library.TryGet(slot.Club, out source) : null;
                    if (bytes != null) source = "your crest: " + source;
                    else
                    {
                        var kit = Kits.For(settings, slot.Club);   // the crest wears the colours of the home kit
                        bytes = CrestWriter.Render(slot.Club.Uid, ClubNamer.ClubName(slot.Club), Kits.Rgb[kit.Home.Shirt], Kits.Rgb[kit.Home.Shorts]);
                        source = "generated";
                    }
                }
                else
                {
                    var original = CrestWriter.FilePath(folder, letter, slot.TemplateIndex + 1);
                    if (File.Exists(original)) bytes = File.ReadAllBytes(original);
                }
                if (bytes == null) continue;
                var fileName = Path.GetFileName(CrestWriter.FilePath(folder, letter, i + 1));
                crests[fileName] = bytes;
                crestRows.Add(new[] { countryKey, fileName, slot.Team.Name, slot.Club != null ? slot.Club.Name : "(original club of the slot)", slot.Group ?? string.Empty, source ?? "original crest" });
            }
        }

        private static List<Slot> BuildSlots(List<TeamPlan> teams, List<int> templateIndexes, LandFile land, RecordFile stad,
            PlayerMapper mapper, StaffPlanner planner, WriteReport report, string label)
        {
            // big stadiums go to high-reputation clubs
            var templates = templateIndexes
                .OrderByDescending(i => StadiumScaler.Total(stad.Records[i]))
                .ThenBy(i => i)
                .ToList();
            // the biggest stadium shape goes to the club with the biggest capacity (the save's ground, or the reputation estimate), then by reputation
            var ordered = teams.OrderByDescending(t => ClubTexts.Capacity(t.Club, mapper.Settings)).ThenByDescending(t => t.Club.Reputation).ToList();
            var result = new List<Slot>();
            for (var k = 0; k < templates.Count; k++)
            {
                var tpl = templates[k];
                if (k >= ordered.Count)
                {
                    report.Warnings.Add(label + ": slot " + (k + 1) + " has no FM club and keeps the original team.");
                    result.Add(new Slot { Team = land.Teams[tpl], Stadium = stad.Records[tpl], TemplateIndex = tpl, Group = label });
                    continue;
                }
                var made = MakeSlot(ordered[k], land.Teams[tpl], stad.Records[tpl], mapper, planner);
                made.Club = ordered[k].Club;
                made.TemplateIndex = tpl;
                made.Group = label;
                result.Add(made);
            }
            if (ordered.Count > templates.Count)
                report.Warnings.Add(label + ": " + (ordered.Count - templates.Count) + " clubs did not fit.");
            return result;
        }

        private static Slot MakeSlot(TeamPlan plan, LandTeam template, string[] stadiumTemplate, PlayerMapper mapper, StaffPlanner planner)
        {
            var club = plan.Club;
            var settings = mapper.Settings;
            var city = ClubTexts.City(club, settings, ClubNamer.MaxCity);
            var team = new LandTeam
            {
                Name = ClubNamer.ClubName(club),
                ShortName = "---",
                City = city,
                Nickname = ClubTexts.Nickname(club, settings),
                Trainer = planner != null ? planner.TrainerFor(club, template.Trainer) : Clone(template.Trainer),
                StaffBlock = Clone(template.StaffBlock)
            };
            foreach (var p in plan.Squad.Select(mapper.Map).OrderBy(p => p.Position1).ThenByDescending(p => p.Strength))
                team.Players.Add(p);
            // the last number of the staff block is the number of player records that follow: the game reads exactly that many
            team.StaffBlock.Values[LandTeam.StaffBlockValueCount - 1] = team.Players.Count;
            // the kits (home shirt, shorts, socks, away shirt, shorts, socks) are staff block numbers 2-7
            var kit = Kits.For(settings, club);
            team.StaffBlock.Values[2] = kit.Home.ShirtValue; team.StaffBlock.Values[3] = kit.Home.Shorts; team.StaffBlock.Values[4] = kit.Home.Socks;
            team.StaffBlock.Values[5] = kit.Away.ShirtValue; team.StaffBlock.Values[6] = kit.Away.Shorts; team.StaffBlock.Values[7] = kit.Away.Socks;

            var stadium = (string[])stadiumTemplate.Clone();
            stadium[0] = ClubTexts.Stadium(club, settings, ClubNamer.MaxStadium);
            stadium[1] = city;
            StadiumScaler.Scale(stadium, ClubTexts.Capacity(club, settings));
            return new Slot { Team = team, Stadium = stadium };
        }

        private static PersonRecord Clone(PersonRecord p)
        {
            return new PersonRecord { Name1 = p.Name1, Name2 = p.Name2, Values = (int[])p.Values.Clone() };
        }

        // ---- Avereine -----------------------------------------------------------------------

        private static AvereineFile BuildAvereine(AvereinePlan plan, string templateDir, FmDataSet fm, PlayerMapper mapper,
            A2Nations nations, WriteReport report)
        {
            var file = (AvereineFile)SavLoader.Load(Path.Combine(templateDir, "Avereine.sav"));
            var count = file.Teams.Count;
            var fresh = new List<AvereineTeam>();
            // the game groups the clubs by nation and needs the file sorted by nation (like the shipped one)
            Func<FmClub, int> countryOf = c => { var n = fm.NationBySaveId(c.NationId); return nations.Resolve(n != null ? n.Name : null); };
            // Fill the shipped layout nation by nation: the FM clubs of a nation take its slots, slots without an FM club (the FM
            // export has no clubs for some small nations) keep the shipped club, so the nation layout stays exactly as the game expects.
            var planByCountry = plan.Teams.GroupBy(t => countryOf(t.Club)).ToDictionary(g => g.Key, g => g.ToList());
            var sortedTeams = new List<TeamPlan>();
            var keptSlots = 0;
            foreach (var group in file.Teams.GroupBy(t => t.Country))
            {
                List<TeamPlan> mine;
                if (!planByCountry.TryGetValue(group.Key, out mine)) mine = new List<TeamPlan>();
                for (var k = 0; k < group.Count(); k++)
                {
                    sortedTeams.Add(k < mine.Count ? mine[k] : null);
                    if (k >= mine.Count) keptSlots++;
                }
                // the nation's champion and cup winner: positions (from 0) among the nation's clubs in the file (country triplets)
                var triplet = file.CountryTriplets.FirstOrDefault(tr => tr.Length == 3 && tr[0] == group.Key);
                var inFile = mine.Take(group.Count()).Select(tp => tp.Club).ToList();
                if (triplet != null && inFile.Count > 0)
                {
                    var chosen = SeasonResults.AssignAvereine(mapper.Settings, SeasonResults.AvereineKey(group.Key), inFile);
                    triplet[1] = inFile.IndexOf(chosen[SeasonResults.Champion]);
                    triplet[2] = inFile.IndexOf(chosen[SeasonResults.CupWinner]);
                }
            }
            if (keptSlots > 0) report.Warnings.Add("Avereine: " + keptSlots + " slots of nations without enough FM clubs keep the original club.");
            var extraByCountry = plan.ExtraClubs.GroupBy(countryOf).ToDictionary(g => g.Key, g => g.ToList());
            var sortedExtras = new List<FmClub>();
            foreach (var group in file.ExtraClubs.GroupBy(e => e.Country))
            {
                List<FmClub> mine;
                if (!extraByCountry.TryGetValue(group.Key, out mine)) mine = new List<FmClub>();
                for (var k = 0; k < group.Count(); k++) sortedExtras.Add(k < mine.Count ? mine[k] : null);
            }
            for (var i = 0; i < count; i++)
            {
                if (i >= sortedTeams.Count || sortedTeams[i] == null)
                {
                    fresh.Add(file.Teams[i]);
                    continue;
                }
                var tp = sortedTeams[i];
                var club = tp.Club;
                var city = ClubTexts.City(club, mapper.Settings, ClubNamer.MaxCity);
                var nation = fm.NationBySaveId(club.NationId);
                var kit = Kits.For(mapper.Settings, club);
                var t = new AvereineTeam
                {
                    Name = ClubNamer.ClubName(club),
                    ShortName = NameShortener.Fit(string.IsNullOrEmpty(club.ShortName) ? city : club.ShortName, ClubNamer.MaxAvereineShortName),
                    Stadium = ClubTexts.Stadium(club, mapper.Settings, 25),
                    City = NameShortener.Fit(city, 14)
                };
                t.HomeShirt = kit.Home.ShirtValue; t.HomeShorts = kit.Home.Shorts; t.HomeSocks = kit.Home.Socks;
                t.AwayShirt = kit.Away.ShirtValue; t.AwayShorts = kit.Away.Shorts; t.AwaySocks = kit.Away.Socks;
                t.Country = nations.Resolve(nation != null ? nation.Name : null);
                t.StadiumCapacity = ClubTexts.Capacity(club, mapper.Settings);
                t.FinancialPower = club.Reputation <= 3500 ? -1 : 0;   // the original data only has 0 and -1
                t.ArticleType = 0;
                t.Flag = 0;
                foreach (var p in tp.Squad.Select(mapper.Map).OrderBy(p => p.Position1).ThenByDescending(p => p.Strength))
                    t.Players.Add(p);
                fresh.Add(t);
            }
            file.Teams.Clear();
            file.Teams.AddRange(fresh);

            for (var i = 0; i < file.ExtraClubs.Count; i++)
            {
                if (i >= sortedExtras.Count || sortedExtras[i] == null) continue;   // keeps the shipped spare club of that nation
                var club = sortedExtras[i];
                var nation = fm.NationBySaveId(club.NationId);
                file.ExtraClubs[i].Name = ClubNamer.ClubName(club);
                file.ExtraClubs[i].Country = nations.Resolve(nation != null ? nation.Name : null);
            }
            return file;
        }

        // ---- youth name pools ---------------------------------------------------------------

        // The twenty youth name pools of the game and the nations they serve (EDITOR.HLP), by Anstoss 2 nation name.
        private static readonly string[][] YouthPools =
        {
            new[] { "Ghana", "Kamerun", "Liberia", "Nigeria", "Senegal", "Zaire" },
            new[] { "Ägypten", "Algerien", "Saudi-Arabien", "Tunesien" },
            new[] { "China", "Japan", "Südkorea" },
            new[] { "Belgien" }, new[] { "Bulgarien" }, new[] { "Dänemark", "Färöer Inseln" }, new[] { "Finnland" },
            new[] { "Griechenland", "Zypern" }, new[] { "Holland" }, new[] { "Israel" }, new[] { "Norwegen" }, new[] { "Polen" },
            new[] { "Brasilien", "Portugal" }, new[] { "Rumänien" },
            new[] { "Estland", "Georgien", "Lettland", "Litauen", "Moldawien", "Rußland", "Ukraine" },
            new[] { "Island", "Schweden" }, new[] { "Albanien", "Kroatien", "Slowenien" }, new[] { "Slowakei", "Tschechien" },
            new[] { "Türkei" }, new[] { "Ungarn" }
        };

        private static string FindTemplateFile(string templateDir, string name)
        {
            var file = Directory.GetFiles(templateDir).Select(Path.GetFileName)
                .FirstOrDefault(f => string.Equals(f, name, StringComparison.OrdinalIgnoreCase));
            return file;
        }

        private static string CountrySuffix(string key)
        {
            return A2Structure.CountrySuffix(key);
        }

        /// <summary>Trainer, assistant, manager, referee and prominent-person files of a country, from the best FM staff of the nation.</summary>
        private static void AddStaffFiles(A2CountrySpec spec, string templateDir, StaffPlanner planner, int teamCount,
            Dictionary<string, SavFile> generated, WriteReport report)
        {
            var kinds = new[]
            {
                new { Prefix = "Trai", Make = (Func<FmStaff, string[]>)StaffBuilder.TrainerRecord },
                new { Prefix = "TTra", Make = (Func<FmStaff, string[]>)StaffBuilder.AssistantRecord },
                new { Prefix = "Mana", Make = (Func<FmStaff, string[]>)StaffBuilder.ManagerRecord },
                new { Prefix = "NSch", Make = (Func<FmStaff, string[]>)StaffBuilder.RefereeRecord },
                new { Prefix = "Prom", Make = (Func<FmStaff, string[]>)(s => StaffBuilder.ProminentRecord(s, spec.Tiers[0].Size)) }
            };
            foreach (var kind in kinds)
            {
                var fileName = FindTemplateFile(templateDir, kind.Prefix + CountrySuffix(spec.Key) + ".sav");
                if (fileName == null) { report.Warnings.Add(spec.Key + ": " + kind.Prefix + " file not found."); continue; }
                var file = (RecordFile)SavLoader.Load(Path.Combine(templateDir, fileName));
                var wanted = file.Records.Count;
                var people = planner.Take(wanted);
                if (people.Count < wanted)
                    report.Warnings.Add(fileName + ": only " + people.Count + " of " + wanted + " FM staff available; the rest keep the original records.");
                var records = people.Select(kind.Make).ToList();
                records.AddRange(file.Records.Skip(records.Count));
                file.Records.Clear();
                file.Records.AddRange(records);
                generated[fileName] = file;
            }
        }

        private static void AddNamePool(string fileName, IEnumerable<string> fmNationNames, string templateDir, FmDataSet fm,
            Dictionary<string, SavFile> generated, WriteReport report)
        {
            var pool = (RecordFile)SavLoader.Load(Path.Combine(templateDir, fileName));
            var nationIds = new HashSet<int>(fmNationNames.Select(fm.NationByName).Where(n => n != null).Select(n => n.SaveId));
            if (nationIds.Count == 0) { report.Warnings.Add(fileName + ": no FM nation found; original names kept."); return; }

            var wanted = pool.Records.Count;
            var names = new List<string[]>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in fm.Players.Where(x => nationIds.Contains(x.NationId)).OrderBy(x => Hash(x.Uid)))
            {
                var first = NameShortener.Fit(p.FirstName ?? string.Empty, NameShortener.MaxFirstName);
                var last = NameShortener.Fit(string.IsNullOrEmpty(p.LastName) ? p.DisplayName : p.LastName, NameShortener.MaxLastName);
                if (first.Length == 0 || last.Length == 0) continue;
                if (!seen.Add(first + "|" + last)) continue;
                names.Add(new[] { first, last });
                if (names.Count >= wanted) break;
            }
            if (names.Count < wanted)
            {
                // too few players of these nations in the save: top up with the names of FM staff of the same nations
                foreach (var s in fm.Staff.Where(x => nationIds.Contains(x.NationId)).OrderBy(x => Hash(x.Uid)))
                {
                    if (names.Count >= wanted) break;
                    var first = NameShortener.Fit(s.FirstName ?? string.Empty, NameShortener.MaxFirstName);
                    var last = NameShortener.Fit(string.IsNullOrEmpty(s.LastName) ? s.DisplayName : s.LastName, NameShortener.MaxLastName);
                    if (first.Length == 0 || last.Length == 0 || !seen.Add(first + "|" + last)) continue;
                    names.Add(new[] { first, last });
                }
            }
            if (names.Count < wanted)
            {
                report.Warnings.Add(fileName + ": only " + names.Count + " of " + wanted + " names available; the rest keep the original names.");
                names.AddRange(pool.Records.Skip(names.Count).Select(r => (string[])r.Clone()));
            }
            pool.Records.Clear();
            pool.Records.AddRange(names.OrderBy(n => n[1], StringComparer.CurrentCultureIgnoreCase).ThenBy(n => n[0], StringComparer.CurrentCultureIgnoreCase));
            generated[fileName] = pool;
        }

        // ---- helpers ------------------------------------------------------------------------

        private static readonly HashSet<string> NoiseWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "FC", "F.C.", "AFC", "SC", "S.C.", "SV", "SSV", "TSV", "VfB", "VfL", "FSV", "SG", "SpVgg", "TuS", "BSV", "KFC", "KRC", "KSV",
            "CF", "CD", "UD", "AS", "AC", "RC", "SS", "US", "SK", "FK", "NK", "HNK", "AEK", "GD", "SD", "CA", "AA", "EC", "SE",
            "Club", "Football", "Futbol", "Fussball", "Calcio", "Sportclub", "Sporting", "Sport", "Real", "Athletic", "Atlético",
            "e.V.", "1.", "A.Ş.", "S.A.D.", "Associazione", "Unione", "Società", "Sportiva", "Deportivo", "Racing", "Olympique"
        };

        // Typical last words of English/Scottish club names that are not the town ("Manchester City" -> Manchester).
        private static readonly HashSet<string> TeamSuffixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "City", "United", "Town", "Rovers", "Albion", "Wanderers", "County", "Hotspur", "Villa", "Forest", "Palace", "Argyle",
            "Orient", "Rangers", "Celtic", "Wednesday", "Borough", "Athletic", "Alexandra", "Harriers", "Stanley", "Hearts", "Thistle",
            "Dynamo", "Wolves", "Saints", "Lions", "Eagles"
        };

        /// <summary>
        /// Placeholder city for a club (FM does not export city names): the club name without common prefixes, reduced to
        /// the most likely place word: the last word, or the first word when the last is a team suffix ("City", "United").
        /// </summary>
        public static string CityFor(FmClub club)
        {
            var tokens = (club.Name ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(t => !NoiseWords.Contains(t) && !t.All(char.IsDigit)).ToList();
            if (tokens.Count == 0) return NameShortener.Fit(club.Name, ClubNamer.MaxCity);
            string city;
            if (tokens.Count == 1) city = tokens[0];
            else
            {
                var last = tokens[tokens.Count - 1];
                city = TeamSuffixes.Contains(last) ? tokens[0] : last;
            }
            return NameShortener.Fit(city, ClubNamer.MaxCity);
        }

        private static int Hash(string text)
        {
            unchecked
            {
                var h = 17;
                foreach (var c in text ?? string.Empty) h = h * 31 + c;
                return h & 0x7FFFFFFF;
            }
        }

        private static void CopyFolder(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (var file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
            foreach (var dir in Directory.GetDirectories(source))
                CopyFolder(dir, Path.Combine(target, Path.GetFileName(dir)));
        }
    }
}
