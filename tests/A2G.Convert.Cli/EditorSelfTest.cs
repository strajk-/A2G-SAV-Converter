using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using A2G.Convert.Engine;
using A2G.Convert.Fm;
using A2G.Convert.Plan;
using A2G.Convert.Writing;

namespace A2G.Convert.Cli
{
    /// <summary>
    /// Checks the plan editor on a real FM export: swaps, pool replacements, squad moves, saving and replaying the edits on a
    /// rebuilt plan, and writing + validating .sav files from the edited plan.
    /// Usage: A2G.Convert.Cli.exe editor-selftest [fm_export folder] [Anstoss 2 folder] [scratch output folder]
    /// </summary>
    internal static class EditorSelfTest
    {
        private static int _failed;

        public static int Run(string[] args)
        {
            var export = args[1];
            var a2 = args[2];
            var scratch = args[3];
            if (Directory.Exists(scratch)) Directory.Delete(scratch, true);
            Directory.CreateDirectory(scratch);

            var fm = FmDataSet.Load(export);
            var settings = new ConversionSettings();
            var plan = PlanBuilder.Build(fm, settings);
            var editor = new PlanEditor(plan, fm, settings);
            var before = Snapshot(plan);

            // 0. limit-aware player edits on a separate copy of the plan (so the replay checks below stay untouched)
            {
                var plan3 = PlanBuilder.Build(fm, settings);
                var ed3 = new PlanEditor(plan3, fm, settings);
                var g3 = ed3.Groups("DEU")[0];
                var tx = g3.Teams[0]; var ty = g3.Teams[1];
                var p0 = tx.Squad.First(p => !p.IsGoalkeeper);
                var moved = ed3.Add(ty, p0, null);
                Check("limits: a player of another squad moves", moved.Ok && moved.Source == tx && ty.Squad.Contains(p0) && !tx.Squad.Contains(p0));
                Check("limits: a player is never in two squads", ed3.AllTeams().Count(t => t.Squad.Contains(p0)) == 1);
                while (tx.Squad.Count > 16) ed3.RemovePlayer(tx, tx.Squad.Last(p => !p.IsGoalkeeper));
                var taken = ed3.Add(ty, tx.Squad.First(p => !p.IsGoalkeeper), null);
                Check("limits: a squad at the minimum cannot give a player away", taken.Result == EditResult.SourceTooSmall && taken.Team == tx && tx.Squad.Count == 16);
                Check("limits: a squad at the minimum cannot remove a player", ed3.Remove(tx, tx.Squad[0]).Result == EditResult.TeamTooSmall && tx.Squad.Count == 16);
                var free3 = fm.Players.Where(p => p.ClubUid == null && p.Age >= SquadSelector.MinAge && ed3.TeamOf(p) == null).ToList();
                while (ty.Squad.Count < 30) ed3.AddPlayer(ty, free3[ty.Squad.Count]);
                var extra = free3[40];
                Check("limits: a full squad refuses another player", ed3.Add(ty, extra, null).Result == EditResult.TargetFull && ty.Squad.Count == 30);
                var leaving = ty.Squad.Last();
                var swapped = ed3.Add(ty, extra, leaving);
                Check("limits: a full squad can replace a player", swapped.Ok && swapped.Replaced == leaving && ty.Squad.Count == 30 && ty.Squad.Contains(extra) && !ty.Squad.Contains(leaving));
                var kid = fm.Players.FirstOrDefault(p => p.Age < SquadSelector.MinAge);
                Check("limits: players under 17 are refused", kid == null || ed3.Add(tx, kid, null).Result == EditResult.TooYoung);
                var aveTeam = ed3.Groups("AVE")[0].Teams[0];
                Check("limits: a non-league squad stays at 11", ed3.Add(aveTeam, free3[50], null).Result == EditResult.TargetFull && ed3.Remove(aveTeam, aveTeam.Squad[0]).Result == EditResult.TeamTooSmall);
            }
            // kits: palette, defaults, the user's choice and the written numbers
            {
                var everyClub = plan.Countries.SelectMany(c => c.AllTeams).Concat(plan.Avereine.Teams).Select(t => t.Club).ToList();
                var valid = everyClub.All(c =>
                {
                    var k = Kits.Default(c);
                    return k.Home.Shirt != k.Away.Shirt && new[] { k.Home, k.Away }.All(s => s.Pattern >= 0 && s.Pattern <= 6 && s.Shirt >= 0 && s.Shirt <= 12 && s.Shorts >= 0 && s.Shorts <= 12 && s.Socks >= 0 && s.Socks <= 12);
                });
                Check("kits: default kits are valid and the home and away shirts differ", valid && Kits.Rgb.Length == Kits.ColourCount);
                var kitClub = plan.Countries[0].AllTeams.First().Club;
                var kitSettings = new ConversionSettings();
                var chosen = Kits.Default(kitClub);
                chosen.Home = new KitSet { Pattern = 2, Shirt = 3, Shorts = 0, Socks = 3 };   // the blue-white hoops of the editor screenshot: 32, 0, 3
                chosen.Away = new KitSet { Pattern = 0, Shirt = 0, Shorts = 0, Socks = 0 };
                Kits.SetOverride(kitSettings, kitClub, chosen);
                Check("kits: the user's kit is stored and read back", Kits.For(kitSettings, kitClub).ToArray().SequenceEqual(chosen.ToArray()) && chosen.Home.ShirtValue == 35);
                var kitFile = Path.Combine(scratch, "kits-settings.json"); kitSettings.Save(kitFile);
                Check("kits: the kit survives saving and loading", Kits.For(ConversionSettings.Load(kitFile), kitClub).ToArray().SequenceEqual(chosen.ToArray()));
                var kitOut = Path.Combine(scratch, "kits-out");
                SavWriter.Write(plan, fm, kitSettings, a2, kitOut);
                var kitLand = Directory.GetFiles(Path.Combine(kitOut, "DATA.DEF"), "Land*.sav").Select(p => (A2G.Sav.LandFile)A2G.Sav.SavLoader.Load(p))
                    .SelectMany(l => l.Teams).FirstOrDefault(team => team.Name == ClubNamer.ClubName(kitClub));
                Check("kits: the numbers are written into the staff block (home 35, 0, 3 / away 0, 0, 0)", kitLand != null
                    && kitLand.StaffBlock.Values[2] == 35 && kitLand.StaffBlock.Values[3] == 0 && kitLand.StaffBlock.Values[4] == 3
                    && kitLand.StaffBlock.Values[5] == 0 && kitLand.StaffBlock.Values[6] == 0 && kitLand.StaffBlock.Values[7] == 0);
                Kits.SetOverride(kitSettings, kitClub, Kits.Default(kitClub));
                Check("kits: the default kit is not stored", kitSettings.FindClubInfo(kitClub.Uid) == null);
                // the kit in the club information file (CSV and Excel): written, read back and applied
                var wantedKit = new ClubKit { Home = new KitSet { Pattern = 4, Shirt = 2, Shorts = 11, Socks = 2 }, Away = new KitSet { Pattern = 0, Shirt = 0, Shorts = 3, Socks = 0 } };
                var kitRow = new ClubInfoRow { Uid = kitClub.Uid, Kit = wantedKit.ToArray().Select(v => v.ToString()).ToArray() };
                var kitCsv = Path.Combine(scratch, "kit-row.csv");
                ClubInfoCsv.Write(kitCsv, new[] { kitRow });
                var csvBack = ClubInfoCsv.Read(kitCsv);
                Check("kits: the kit columns survive the CSV", csvBack.Count == 1 && csvBack[0].Kit.SequenceEqual(kitRow.Kit));
                var importSettings = new ConversionSettings();
                var imported = ClubInfoCsv.Apply(importSettings, fm, csvBack);
                Check("kits: an imported kit is applied", imported.Changed == 1 && Kits.For(importSettings, kitClub).ToArray().SequenceEqual(wantedKit.ToArray()));
                var kitXlsx = Path.Combine(scratch, "kit-row.xlsx");
                ClubInfoFile.Write(kitXlsx, new[] { kitRow }, new ClubInfoSheetText());
                var xlsxBack = ClubInfoFile.Read(kitXlsx);
                Check("kits: the kit columns survive the Excel file", xlsxBack.Count == 1 && xlsxBack[0].Kit.Select(v => (v ?? string.Empty).Trim()).SequenceEqual(kitRow.Kit));
                var emptyKit = new ClubInfoRow { Uid = kitClub.Uid, Kit = new string[8] };
                var keepSettings = new ConversionSettings();
                Check("kits: empty or invalid kit cells keep the current kit", ClubInfoCsv.Apply(keepSettings, fm, new[] { emptyKit }).Changed == 0
                    && ClubInfoCsv.ParseKit(new[] { "9", "0", "0", "0", "0", "0", "0", "0" }) == null && ClubInfoCsv.ParseKit(new[] { "0", "13", "0", "0", "0", "0", "0", "0" }) == null);
                // the shared library: project A stores kit, texts and a crest; a new project B gets them; removals reach the library
                SharedLibrary.Root = Path.Combine(scratch, "library");
                var projA = new ConversionSettings();
                var syncedA = new HashSet<string>();
                Kits.SetOverride(projA, kitClub, wantedKit);
                ClubTexts.SetOverride(projA, kitClub, "Teststadt", "Testers", "Test Arena", 12345);
                SharedLibrary.Sync(projA, syncedA, true);
                var folderA = Path.Combine(scratch, "crestsA"); var folderB = Path.Combine(scratch, "crestsB");
                Directory.CreateDirectory(folderA);
                File.WriteAllBytes(Path.Combine(folderA, kitClub.Uid + ".bmp"), A2G.Convert.Writing.CrestWriter.Render(kitClub.Uid, "Test"));
                SharedLibrary.SyncCrests(folderA, true);
                var projB = new ConversionSettings();
                var syncedB = new HashSet<string>();
                var addedB = SharedLibrary.Sync(projB, syncedB, true);
                var crestsB = SharedLibrary.SyncCrests(folderB, true);
                Check("library: a new project gets kit, texts and crest of the club", addedB == 1 && Kits.For(projB, kitClub).ToArray().SequenceEqual(wantedKit.ToArray())
                    && ClubTexts.Stadium(kitClub, projB, 30) == "Test Arena" && ClubTexts.Capacity(kitClub, projB) == 12345 && crestsB == 1 && File.Exists(Path.Combine(folderB, kitClub.Uid + ".bmp")));
                Kits.SetOverride(projB, kitClub, Kits.Default(kitClub));
                ClubTexts.SetOverride(projB, kitClub, "", "", "", ClubTexts.DefaultCapacity(kitClub));
                SharedLibrary.Sync(projB, syncedB, false);
                Check("library: resetting a club in a project removes it from the library", SharedLibrary.LoadClubs().Count == 0);
                SharedLibrary.Root = null;
                // results of the last season: the values the game's editor shows in its legend
                Check("season: the values of the original data decode (70 = runner-up + cup winner, 65 = champion + cup winner, 19 = relegated)",
                    SeasonResults.FromCode(70) == LeagueResult.RunnerUp && SeasonResults.FromCode(65) == LeagueResult.Champion && SeasonResults.FromCode(19) == LeagueResult.Relegated
                    && SeasonResults.Encode(16, new SeasonResult { League = LeagueResult.Promoted }) == 20
                    && SeasonResults.Encode(0, new SeasonResult { League = LeagueResult.RunnerUp, Cup = CupResult.Winner }) == 70
                    && SeasonResults.Encode(0, new SeasonResult { League = LeagueResult.Champion, Cup = CupResult.Finalist }) == 129);
                var germany = plan.Countries.First(c => c.Spec.Key == "DEU");
                var top = germany.Tiers[0].Leagues[0];
                var second = germany.Tiers[1].Leagues[0];
                var topCounts = SeasonResults.CountsFor(a2, germany.Spec, top.Spec);
                var secondCounts = SeasonResults.CountsFor(a2, germany.Spec, second.Spec);
                Check("season: the numbers of places come from the original data (Bundesliga 3 UEFA places and 3 promoted, 2. Liga 3 relegated and 4 promoted)",
                    topCounts.Uefa == 3 && topCounts.Promoted == 3 && secondCounts.Relegated == 3 && secondCounts.Promoted == 4);
                var topClubs = top.Teams.Select(t => t.Club).ToList();
                var topKey = SeasonResults.LeagueKey(top.Spec);
                var auto = SeasonResults.Results(SeasonResults.AutoSlots(topClubs, true, topCounts));
                var weakest = topClubs.OrderBy(c => c.Reputation).Take(3).ToList();
                Check("season: the weakest clubs are the promoted ones, one champion and one runner-up, the champion is the cup winner",
                    auto.Count(kv => kv.Value.League == LeagueResult.Promoted) == 3 && weakest.All(c => auto[c].League == LeagueResult.Promoted)
                    && auto.Count(kv => kv.Value.League == LeagueResult.Champion) == 1 && auto.Count(kv => kv.Value.League == LeagueResult.RunnerUp) == 1
                    && auto.Count(kv => kv.Value.League == LeagueResult.UefaPlace) == 3
                    && auto.Single(kv => kv.Value.League == LeagueResult.Champion).Value.Cup == CupResult.Winner);
                var secondAuto = SeasonResults.Results(SeasonResults.AutoSlots(second.Teams.Select(t => t.Club).ToList(), false, secondCounts));
                Check("season: in the second division the strongest clubs are relegated, the weakest promoted",
                    secondAuto.Count(kv => kv.Value.League == LeagueResult.Relegated) == 3 && secondAuto.Count(kv => kv.Value.League == LeagueResult.Promoted) == 4
                    && secondAuto.All(kv => kv.Value.League != LeagueResult.Champion));
                var seasonSettings = new ConversionSettings();
                var other = auto.First(kv => kv.Value.League == LeagueResult.UefaPlace).Key;
                var oldChampion = auto.Single(kv => kv.Value.League == LeagueResult.Champion).Key;
                SeasonResults.SetSlot(seasonSettings, topKey, SeasonResults.Champion, other);
                var changed = SeasonResults.Effective(seasonSettings, topKey, topClubs, true, topCounts);
                Check("season: a club picked for the champion slot takes it and swaps places with the old champion (still exactly one champion, three UEFA places)",
                    changed[other].League == LeagueResult.Champion && changed[oldChampion].League == LeagueResult.UefaPlace
                    && changed.Count(kv => kv.Value.League == LeagueResult.Champion) == 1 && changed.Count(kv => kv.Value.League == LeagueResult.UefaPlace) == 3);
                SeasonResults.SetSlot(seasonSettings, topKey, SeasonResults.CupWinner, topClubs.First(c => c != oldChampion && c != other));
                var cupChanged = SeasonResults.Effective(seasonSettings, topKey, topClubs, true, topCounts);
                Check("season: the cup winner is a separate slot (the champion keeps the league result without the cup)",
                    cupChanged.Count(kv => kv.Value.Cup == CupResult.Winner) == 1 && cupChanged[other].Cup != CupResult.Winner && cupChanged[other].League == LeagueResult.Champion);
                SeasonResults.ResetGroup(seasonSettings, topKey);
                Check("season: going back to the rules removes the entries", seasonSettings.SeasonSlots.Count == 0);
                var nationClubs = fm.Clubs.Values.Where(c => c.IsMainMensClub && c.Reputation > 3000).Take(4).ToList();
                var aveKey = SeasonResults.AvereineKey(6);
                SeasonResults.SetSlot(seasonSettings, aveKey, SeasonResults.Champion, nationClubs[3]);
                var aveSlots = SeasonResults.AssignAvereine(seasonSettings, aveKey, nationClubs);
                Check("season: a nation's champion can be chosen, the cup winner stays with the rules", aveSlots[SeasonResults.Champion] == nationClubs[3] && aveSlots[SeasonResults.CupWinner] != null);
                SeasonResults.ResetGroup(seasonSettings, aveKey);
                // written into the files
                var seasonOut = Path.Combine(scratch, "season-out");
                SavWriter.Write(plan, fm, seasonSettings, a2, seasonOut);
                var deLand = (A2G.Sav.LandFile)A2G.Sav.SavLoader.Load(Path.Combine(seasonOut, "DATA.DEF", germany.Spec.LandFile));
                var deLiga = (A2G.Sav.LigaFile)A2G.Sav.SavLoader.Load(Path.Combine(seasonOut, "DATA.DEF", top.Spec.LigaFile));
                var written = deLiga.TeamIds.Select(id => deLand.Teams[id - 1].StaffBlock.Values[SeasonResults.FlagIndex]).ToList();
                var champClub = auto.Single(kv => kv.Value.League == LeagueResult.Champion).Key;
                var champTeam = deLand.Teams.First(t => t.Name == ClubNamer.ClubName(champClub));
                Check("season: the Bundesliga flags are written (1 champion with the cup, 1 runner-up, 3 UEFA places, 3 promoted)",
                    written.Count(v => (v & 7) == 1) == 1 && written.Count(v => (v & 7) == 6) == 1 && written.Count(v => (v & 7) == 2) == 3 && written.Count(v => (v & 7) == 4) == 3
                    && (champTeam.StaffBlock.Values[SeasonResults.FlagIndex] & 65) == 65);
                // the non-league clubs: champion and cup winner of a nation as positions among its clubs
                var aveFile = (A2G.Sav.AvereineFile)A2G.Sav.SavLoader.Load(Path.Combine(seasonOut, "DATA.DEF", "Avereine.sav"));
                var tripletOk = true;
                foreach (var tr in aveFile.CountryTriplets)
                {
                    var members = aveFile.Teams.Where(t => t.Country == tr[0]).ToList();
                    if (tr[1] < 0 || tr[1] >= members.Count || tr[2] < 0 || tr[2] >= members.Count) tripletOk = false;
                }
                Check("season: the Avereine triplets point at clubs of their nation", tripletOk && aveFile.CountryTriplets.Count == 30);
            }
            // the year the game starts in lives in KLEINIG.SAV
            {
                var yearOut = Path.Combine(scratch, "year-out");
                SavWriter.Write(plan, fm, new ConversionSettings { StartYear = 2025 }, a2, yearOut);
                var latin1 = System.Text.Encoding.GetEncoding(28591);
                var original = File.ReadAllLines(Path.Combine(a2, "DATA.DEF", "KLEINIG.SAV"), latin1);
                var written = File.ReadAllLines(Path.Combine(yearOut, "DATA.DEF", "KLEINIG.SAV"), latin1);
                var writtenVer = File.ReadAllLines(Path.Combine(yearOut, "DATA.VER", "KLEINIG.SAV"), latin1);
                var line = GameYear.FindYearLine(original);
                Check("start year: the year line is found in the shipped file", line > 0 && original[line] == "1998");
                Check("start year: only that line changes (DATA.DEF and DATA.VER)", written.Length == original.Length && writtenVer.Length == original.Length
                    && written[line] == "2025" && writtenVer[line] == "2025" && Enumerable.Range(0, original.Length).Where(i => i != line).All(i => original[i] == written[i]));
                var keepOut = Path.Combine(scratch, "year-keep");
                SavWriter.Write(plan, fm, new ConversionSettings(), a2, keepOut);
                Check("start year: without a year the file stays as shipped", File.ReadAllBytes(Path.Combine(keepOut, "DATA.DEF", "KLEINIG.SAV")).SequenceEqual(File.ReadAllBytes(Path.Combine(a2, "DATA.DEF", "KLEINIG.SAV"))));
                Check("start year: the year of an FM date is read", GameYear.FromFmDate("2025-07-21") == 2025 && GameYear.FromFmDate("") == 0);
            }
            // names: a common name that holds the first name is split, surnames stay within the 15 characters the game shows correctly
            {
                var mapper = new PlayerMapper(new ConversionSettings(), fm, new A2Nations(new[] { "Sonstige" }));
                var moscardo = fm.Players.FirstOrDefault(p => p.Uid == "2000208419");
                if (moscardo != null)
                {
                    var mapped = mapper.Map(moscardo);
                    Check("names: 'Gabriel Moscardo' becomes first name Gabriel, surname Moscardo", mapped.FirstName == "Gabriel" && mapped.LastName == "Moscardo");
                }
                var longest = plan.Countries.SelectMany(c => c.AllTeams).Concat(plan.Avereine.Teams).SelectMany(t => t.Squad).Select(p => mapper.Map(p).LastName.Length).DefaultIfEmpty(0).Max();
                Check("names: no surname is longer than 15 characters", longest <= 15 && NameShortener.MaxLastName == 15);
            }
            // skin and hair: nationality rule and per-player overrides
            {
                var looks = new ConversionSettings();
                Func<string, int[]> skinShare = nationName =>
                {
                    var nation = fm.NationByName(nationName);
                    var ps = nation == null ? new System.Collections.Generic.List<A2G.Convert.Fm.FmPlayer>() : fm.Players.Where(p => p.NationId == nation.SaveId).Take(400).ToList();
                    var counts = new int[3];
                    foreach (var p in ps) counts[PlayerLook.Skin(looks, fm, p)]++;
                    return new[] { counts[0], counts[1], counts[2], ps.Count };
                };
                var ghana = skinShare("Ghana");
                var japan = skinShare("Japan");
                Check("looks: Ghanaian players are mostly dark, Japanese players light", ghana[3] == 0 || ghana[2] * 10 >= ghana[3] * 8);
                Check("looks: Japanese players are mostly light", japan[3] == 0 || japan[0] * 10 >= japan[3] * 8);
                var someone = fm.Players.First();
                var skinBefore = PlayerLook.Skin(looks, fm, someone);
                var wantedSkin = (skinBefore + 1) % 3;
                PlayerLook.SetOverride(looks, fm, someone, wantedSkin, 5);
                Check("looks: a player's own skin and hair win", PlayerLook.Skin(looks, fm, someone) == wantedSkin && PlayerLook.Hair(looks, fm, someone) == 5);
                var looksFile = Path.Combine(scratch, "looks-settings.json");
                looks.Save(looksFile);
                var looksBack = ConversionSettings.Load(looksFile);
                Check("looks: the overrides survive saving and loading", PlayerLook.Skin(looksBack, fm, someone) == wantedSkin && PlayerLook.Hair(looksBack, fm, someone) == 5);
                PlayerLook.ClearOverride(looks, someone);
                Check("looks: resetting returns to the default", PlayerLook.Skin(looks, fm, someone) == skinBefore && looks.FindLook(someone.Uid) == null);
                var off = new ConversionSettings { SkinFromNationality = false };
                Check("looks: with the rule off every player is light", fm.Players.Take(300).All(p => PlayerLook.Skin(off, fm, p) == 0));
            }
            // club texts the FM save does not have: defaults, user overrides, persistence and the written files
            {
                var clubTexts = new ConversionSettings();
                var club0 = plan.Countries[0].AllTeams.First().Club;
                Check("club texts: the default city comes from the club name", ClubTexts.City(club0, clubTexts, 16) == SavWriter.CityFor(club0));
                clubTexts.ClubInfo.Add(new ClubInfoOverride { Uid = club0.Uid, City = "Testburg", Stadium = "Teststadion" });
                Check("club texts: the user's city, stadium and a matching nickname are used",
                    ClubTexts.City(club0, clubTexts, 16) == "Testburg" && ClubTexts.Stadium(club0, clubTexts, 26) == "Teststadion" && ClubTexts.Nickname(club0, clubTexts) == "Testburg");
                var settingsFile = Path.Combine(scratch, "club-texts-settings.json");
                clubTexts.Save(settingsFile);
                var reloaded = ConversionSettings.Load(settingsFile);
                var kept = reloaded.FindClubInfo(club0.Uid);
                Check("club texts: overrides survive saving and loading", kept != null && kept.City == "Testburg" && kept.Stadium == "Teststadion");
                var textOut = Path.Combine(scratch, "club-texts-out");
                SavWriter.Write(plan, fm, clubTexts, a2, textOut);
                var landFile = Directory.GetFiles(Path.Combine(textOut, "DATA.DEF"), "Land*.sav").Select(p => (A2G.Sav.LandFile)A2G.Sav.SavLoader.Load(p))
                    .FirstOrDefault(l => l.Teams.Any(team => team.City == "Testburg"));
                Check("club texts: the written Land file has the user's city", landFile != null);
            }
            // club information CSV: export, edit in a spreadsheet, import
            {
                var csvSettings = new ConversionSettings();
                var clubs = plan.Countries.SelectMany(c => c.AllTeams).Select(t => t.Club).Take(5).ToList();
                var rows = clubs.Select(c => new ClubInfoRow
                {
                    Uid = c.Uid, Nation = "Germany", League = "1. Liga", FmName = c.Name, WrittenName = ClubNamer.ClubName(c),
                    City = ClubTexts.City(c, csvSettings, 16), Nickname = ClubTexts.Nickname(c, csvSettings), Stadium = ClubTexts.Stadium(c, csvSettings, 26)
                }).ToList();
                rows[1].City = "Muenchen; \"Bayern\"";            // a semicolon and quotes must survive the round trip
                rows[2].Stadium = "Arena, Nord";
                var csvPath = Path.Combine(scratch, "club-information.csv");
                ClubInfoCsv.Write(csvPath, rows);
                var back = ClubInfoCsv.Read(csvPath);
                Check("csv: all rows come back", back.Count == rows.Count && back.Select(r => r.Uid).SequenceEqual(rows.Select(r => r.Uid)));
                Check("csv: semicolons, quotes and commas survive", back[1].City == rows[1].City && back[2].Stadium == rows[2].Stadium);
                // edit the file like a user would: a new city for the first club, an empty nickname (= default), an unknown id
                var oldNickname = rows[0].Nickname;
                rows[0].City = "Neustadt";                         // only the city cell is edited; the nickname cell keeps its old text
                ClubInfoCsv.Write(csvPath, rows);
                File.AppendAllText(csvPath, "no-such-club;X;Y;Z;W;City;Nick;Stad\r\n");
                var editedRows = ClubInfoCsv.Read(csvPath);
                var csvResult = ClubInfoCsv.Apply(csvSettings, fm, editedRows);
                Check("csv: an unknown club id is counted, not applied", csvResult.Unknown == 1);
                Check("csv: an edited city is applied to that club", ClubTexts.City(clubs[0], csvSettings, 16) == "Neustadt" && ClubTexts.Nickname(clubs[0], csvSettings) == oldNickname);
                Check("csv: unedited clubs stay at their defaults", csvSettings.FindClubInfo(clubs[3].Uid) == null && csvSettings.FindClubInfo(clubs[4].Uid) == null);
                var blank = new ConversionSettings();
                ClubInfoCsv.Apply(blank, fm, new[] { new ClubInfoRow { Uid = clubs[0].Uid, City = "", Nickname = "", Stadium = "" } });
                Check("csv: empty cells mean the default", blank.FindClubInfo(clubs[0].Uid) == null);
            }
            // club information as an Excel workbook: every club of the plan, special characters, read back through the same code path
            {
                var xl = new ConversionSettings();
                var allClubs = plan.Countries.SelectMany(c => c.AllTeams).Concat(plan.Avereine.Teams).Select(t => t.Club).ToList();
                var xlRows = allClubs.Select(c => new ClubInfoRow
                {
                    Uid = c.Uid, Nation = "Germany", League = "1. Liga", FmName = c.Name, WrittenName = ClubNamer.ClubName(c),
                    City = ClubTexts.City(c, xl, 16), Nickname = ClubTexts.Nickname(c, xl), Stadium = ClubTexts.Stadium(c, xl, 26)
                }).ToList();
                xlRows[0].City = "A & B <c> \"d\"; e";
                xlRows[1].Stadium = "  Arena Süd  ";
                var xlPath = Path.Combine(scratch, "club-information.xlsx");
                ClubInfoFile.Write(xlPath, xlRows, new ClubInfoSheetText { InfoTitle = "Club information", InfoLines = { "line one", "line two" } });
                var xlBack = ClubInfoFile.Read(xlPath);
                Check("xlsx: all clubs of the plan come back in order", xlBack.Count == xlRows.Count && xlBack.Select(r => r.Uid).SequenceEqual(xlRows.Select(r => r.Uid)));
                Check("xlsx: ampersands, brackets, quotes and umlauts survive", xlBack[0].City == xlRows[0].City && xlBack[1].Stadium.Trim() == "Arena Süd" && xlBack[2].FmName == xlRows[2].FmName);
                Check("xlsx: the editable columns read back as written", xlBack.All(r => true) && xlBack[3].City == xlRows[3].City && xlBack[3].Nickname == xlRows[3].Nickname);
                var xlApplied = ClubInfoCsv.Apply(xl, fm, xlBack);
                Check("xlsx: importing the unedited workbook changes nothing but the two edited cells", xlApplied.Unknown == 0 && xlApplied.Changed <= 2);
            }
            // generated crests
            {
                var bmp = CrestWriter.Render("42", "FC Bayern München");
                Check("crest: 2102-byte 32x32 8-bit BMP like the originals", bmp.Length == 2102 && bmp[0] == 'B' && BitConverter.ToInt32(bmp, 18) == 32 && bmp[28] == 8);
                Check("crest: letters from the significant words", CrestWriter.Letters("FC Bayern München") == "BM" && CrestWriter.Letters("Arsenal") == "AR" && CrestWriter.Letters("Borussia Mönchengladbach") == "BM"
                    && CrestWriter.Letters("Real Sociedad de Fútbol") == "RSF" && CrestWriter.Letters("1. FC Köln") == "KO");
            }
            // community crests: any image named by the club id is converted to the game format
            {
                var crestDir = Path.Combine(scratch, "crests");
                Directory.CreateDirectory(crestDir);
                var clubA = plan.Countries[0].AllTeams.First().Club;
                var clubB = plan.Countries[0].AllTeams.Skip(1).First().Club;
                using (var bmp = new System.Drawing.Bitmap(120, 90))
                using (var g = System.Drawing.Graphics.FromImage(bmp))
                {
                    g.Clear(System.Drawing.Color.Transparent);
                    g.FillEllipse(System.Drawing.Brushes.Red, 10, 5, 100, 80);
                    for (var i = 0; i < 90; i++) bmp.SetPixel(i, i, System.Drawing.Color.FromArgb(i * 2, 255 - i, i)); // many colours
                    bmp.Save(Path.Combine(crestDir, clubA.Uid + " - test crest.png"), System.Drawing.Imaging.ImageFormat.Png);
                }
                File.WriteAllBytes(Path.Combine(crestDir, clubB.Uid + ".bmp"), CrestWriter.Render("7", "Test Club"));
                var lib = new CrestLibrary(crestDir);
                string srcA, srcB;
                var bytesA = lib.TryGet(clubA, out srcA);
                var bytesB = lib.TryGet(clubB, out srcB);
                Check("crest library: finds the files by club id", lib.Count == 2 && srcA != null && srcA.StartsWith(clubA.Uid) && srcB == clubB.Uid + ".bmp");
                Check("crest library: a png becomes a 2102-byte 32x32 8-bit BMP", bytesA != null && bytesA.Length == 2102 && BitConverter.ToInt32(bytesA, 18) == 32 && bytesA[28] == 8);
                Check("crest library: a game BMP is used unchanged", bytesB != null && bytesB.SequenceEqual(CrestWriter.Render("7", "Test Club")));
                Check("crest library: a club without a file gets none", lib.TryGet(plan.Countries[0].AllTeams.Skip(2).First().Club, out srcA) == null);
            }
            // stadium capacity: reputation default, override, file round trip, scaler
            {
                var cs = new ConversionSettings();
                var c0 = plan.Countries.SelectMany(c => c.AllTeams).Select(t => t.Club).OrderByDescending(c => c.Reputation).First();
                var cLow = plan.Avereine.Teams.Select(t => t.Club).OrderBy(c => c.Reputation).First();
                Check("capacity: default from reputation within limits, big clubs bigger",
                    ClubTexts.Capacity(c0, cs) >= ClubTexts.Capacity(cLow, cs) && ClubTexts.Capacity(cLow, cs) >= ClubTexts.MinCapacity && ClubTexts.Capacity(c0, cs) <= ClubTexts.MaxCapacity
                    && ClubTexts.Capacity(c0, cs) % 100 == 0);
                ClubTexts.SetOverride(cs, c0, null, null, null, 51500);
                Check("capacity: override is used and the default is not stored", ClubTexts.Capacity(c0, cs) == 51500
                    && ClubTexts.SetOverride(cs, c0, null, null, null, ClubTexts.DefaultCapacity(c0)) && cs.FindClubInfo(c0.Uid) == null);
                var rowsCap = new[] { new ClubInfoRow { Uid = c0.Uid, Capacity = "62.500" } };
                ClubInfoCsv.Apply(cs, fm, rowsCap);
                Check("capacity: text with thousands separator is imported", ClubTexts.Capacity(c0, cs) == 62500);
                var capPath = Path.Combine(scratch, "capacity.xlsx");
                ClubInfoFile.Write(capPath, new[] { new ClubInfoRow { Uid = c0.Uid, Capacity = "47000" } }, new ClubInfoSheetText());
                Check("capacity: xlsx round trip", ClubInfoFile.Read(capPath)[0].Capacity.Trim() == "47000");
                foreach (var target in new[] { 3000, 12345, 51500, 110000 })
                {
                    var rec = new string[31];
                    rec[0] = "S"; rec[1] = "C";
                    var rnd = new Random(target);
                    for (var i = 2; i < 31; i++) rec[i] = (i < 26 ? rnd.Next(0, 4000) : 7).ToString();
                    StadiumScaler.Scale(rec, target);
                    var want = target / 100 * 100;
                    Check("capacity: scaler reaches " + want, StadiumScaler.Total(rec) == want && Enumerable.Range(2, 24).All(i => int.Parse(rec[i]) <= StadiumScaler.MaxSection));
                }
            }
            // 1. swap two teams between the Bundesliga and the 2. Liga
            var deu = editor.Groups("DEU");
            var bl1 = deu[0]; var bl2 = deu[1];
            var a = bl1.Teams[17]; var b = bl2.Teams[0];
            editor.SwapTeams(bl1, 17, bl2, 0);
            Check("swap exchanges the clubs", bl1.Teams[17] == b && bl2.Teams[0] == a);
            Check("swap keeps the league sizes", bl1.Teams.Count == 18 && bl2.Teams.Count == 18);
            Check("swap is limited to one country", Throws(() => editor.SwapTeams(bl1, 0, editor.Groups("ENG")[0], 0)));

            // 2. replace a spare team by a pool club
            var spare = deu.Last();
            var pool = editor.Pool("DEU");
            Check("pool has unused German clubs", pool.Count > 0 && pool.All(c => plan.Countries[0].AllTeams.All(t => t.Club != c)));
            var oldSpare = spare.Teams[0].Club;
            var newClub = pool[0];
            editor.ReplaceWithPoolClub(spare, 0, newClub);
            Check("replace puts the pool club in the slot", spare.Teams[0].Club == newClub);
            Check("replaced club returns to the pool", editor.Pool("DEU").Contains(oldSpare) && !editor.Pool("DEU").Contains(newClub));
            Check("new team has a valid default squad", editor.Check(spare, spare.Teams[0]).Count == 0);
            Check("a club cannot be used twice", Throws(() => editor.ReplaceWithPoolClub(spare, 1, newClub)));

            // 3. squads: move a player from one team to another
            var teamX = bl1.Teams[0]; var teamY = bl1.Teams[1];
            var mover = teamX.Squad.First(p => !p.IsGoalkeeper);
            var xCount = teamX.Squad.Count; var yCount = teamY.Squad.Count;
            editor.AddPlayer(teamY, mover);
            Check("player moved to the other team", teamY.Squad.Contains(mover) && !teamX.Squad.Contains(mover));
            Check("squad sizes follow", teamX.Squad.Count == xCount - 1 && teamY.Squad.Count == yCount + 1);
            Check("a player is in only one squad", editor.AllTeams().Count(t => t.Squad.Contains(mover)) == 1);
            foreach (var p in teamY.Squad.Where(p => !p.IsGoalkeeper).Skip(8).ToList()) editor.RemovePlayer(teamY, p);
            Check("too small a squad is reported", editor.Check(bl1, teamY).Any(m => m.Contains("at least")));
            foreach (var p in fm.PlayersOf(teamY.Club).Where(p => !teamY.Squad.Contains(p)).Take(30)) editor.AddPlayer(teamY, p);
            Check("too big a squad is reported", editor.Check(bl1, teamY).Any(m => m.Contains("at most")) || teamY.Squad.Count <= 30);

            // restore a sensible squad for the write test
            foreach (var p in teamY.Squad.Skip(24).ToList()) editor.RemovePlayer(teamY, p);

            // 4. non-league clubs
            var ave = editor.Groups("AVE")[0];
            var avePool = editor.Pool("AVE");
            Check("non-league pool has clubs", avePool.Count > 0);
            // a team whose nation still has an unused club in the pool (small nations have none)
            var aveIndex = Enumerable.Range(0, ave.Teams.Count).First(k => avePool.Any(c => AvereineSlots.SlotOf(fm, c, AvereineSlots.Clubs) == AvereineSlots.SlotOf(fm, ave.Teams[k].Club, AvereineSlots.Clubs)));
            var oldSlot = AvereineSlots.SlotOf(fm, ave.Teams[aveIndex].Club, AvereineSlots.Clubs);
            Check("non-league replacement by another nation is refused", Throws(() => editor.ReplaceWithPoolClub(ave, aveIndex,
                avePool.First(c => AvereineSlots.SlotOf(fm, c, AvereineSlots.Clubs) != oldSlot))));
            editor.ReplaceWithPoolClub(ave, aveIndex, avePool.First(c => AvereineSlots.SlotOf(fm, c, AvereineSlots.Clubs) == oldSlot));
            Check("non-league replacement keeps 11 players", ave.Teams[aveIndex].Squad.Count == settings.AvereineSquadSize && editor.Check(ave, ave.Teams[aveIndex]).Count == 0);

            // 5. save the edits, rebuild the plan, replay
            var logPath = Path.Combine(scratch, "overrides.json");
            editor.Log.Save(logPath);
            var edited = Snapshot(plan);
            var plan2 = PlanBuilder.Build(fm, settings);
            Check("a rebuilt plan differs from the edited one", Snapshot(plan2) != edited);
            var editor2 = new PlanEditor(plan2, fm, settings);
            var applied = editor2.Replay(EditLog.Load(logPath));
            Console.WriteLine("  (replayed {0} of {1} edits)", applied, editor.Log.Ops.Count);
            Check("all edits could be replayed", applied == editor.Log.Ops.Count);
            Check("replaying restores the edited plan", Snapshot(plan2) == edited);
            Check("the edited plan differs from the original", before != edited);

            // 6. write .sav files from the edited plan and validate them
            var outDir = Path.Combine(scratch, "Anstoss2");
            var report = SavWriter.Write(plan2, fm, settings, a2, outDir);
            var errors = SavValidator.Validate(Path.Combine(outDir, "DATA.DEF"), Path.Combine(a2, "DATA.DEF"));
            foreach (var e in errors.Take(10)) Console.WriteLine("    " + e);
            Check("files written from the edited plan validate", errors.Count == 0);

            Console.WriteLine();
            Console.WriteLine(_failed == 0 ? "All editor checks passed." : _failed + " editor check(s) FAILED.");
            return _failed == 0 ? 0 : 4;
        }

        /// <summary>A text fingerprint of who plays where (clubs per group and squad members).</summary>
        private static string Snapshot(ConversionPlan plan)
        {
            var sb = new System.Text.StringBuilder();
            Action<string, System.Collections.Generic.IEnumerable<TeamPlan>> add = (key, teams) =>
            {
                sb.Append(key).Append(':');
                foreach (var t in teams.OrderBy(x => x.Club.Uid, StringComparer.Ordinal))
                    sb.Append(t.Club.Uid).Append('[').Append(string.Join(",", t.Squad.Select(p => p.Uid).OrderBy(x => x, StringComparer.Ordinal))).Append(']');
                sb.AppendLine();
            };
            foreach (var c in plan.Countries)
            {
                foreach (var tier in c.Tiers) foreach (var l in tier.Leagues) add(c.Spec.Key + l.Spec.LigaFile, l.Teams);
                add(c.Spec.Key + "spare", c.Spares);
            }
            add("AVE", plan.Avereine.Teams);
            return sb.ToString();
        }

        private static void Check(string what, bool ok)
        {
            Console.WriteLine("  {0} {1}", ok ? "PASS" : "FAIL", what);
            if (!ok) _failed++;
        }

        private static bool Throws(Action action)
        {
            try { action(); return false; } catch (Exception) { return true; }
        }
    }
}
