using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using A2G.Convert.Engine;
using A2G.Convert.Fm;
using A2G.Convert.Plan;
using A2G.Sav;

namespace A2G.Convert.Cli
{
    /// <summary>
    /// Builds a conversion plan from an fm_export folder and prints a report (nothing is written).
    /// Usage: A2G.Convert.Cli.exe [fm_export folder] [Anstoss 2 folder]
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            if (args.Length == 4 && args[0] == "profiles-selftest") return ProfileSelfTest.Run(args);
            if (args.Length == 4 && args[0] == "editor-selftest") return EditorSelfTest.Run(args);
            if (args.Length == 4 && args[0] == "patch-selftest") return PatchSelfTest.Run(args);
            if (args.Length == 2 && args[0] == "project-selftest") return ProjectSelfTest.Run(args);
            var exportDir = args.Length > 0 ? args[0] : Path.Combine("Projects", "Default", "fm_export");
            // the Anstoss 2 folder: second argument, else the environment variable A2G_ANSTOSS2
            var a2Dir = args.Length > 1 ? args[1] : Environment.GetEnvironmentVariable("A2G_ANSTOSS2");
            if (string.IsNullOrWhiteSpace(a2Dir))
            {
                Console.WriteLine("Give the Anstoss 2 folder as the second argument or set the environment variable A2G_ANSTOSS2.");
                return 2;
            }

            var sw = Stopwatch.StartNew();
            var fm = FmDataSet.Load(exportDir);
            Console.WriteLine("Loaded {0} clubs, {1} players, {2} nations, {3} leagues in {4:0.0}s",
                fm.Clubs.Count, fm.Players.Count, fm.Nations.Count, fm.Leagues.Count, sw.Elapsed.TotalSeconds);

            var settings = new ConversionSettings();
            var plan = PlanBuilder.Build(fm, settings);

            foreach (var country in plan.Countries)
            {
                Console.WriteLine();
                Console.WriteLine("=== {0} ({1} teams expected {2})", country.Spec.Key, country.AllTeams.Count(), country.Spec.TeamCount);
                foreach (var tier in country.Tiers)
                    foreach (var league in tier.Leagues)
                        Console.WriteLine("  {0,-22} {1,2}/{2,-2} avg rep {3,5:0}  {4}", league.Spec.Name, league.Teams.Count, league.Spec.Size,
                            league.Teams.Count == 0 ? 0 : league.Teams.Average(t => t.Club.Reputation),
                            string.Join(", ", league.Teams.Take(4).Select(t => t.Club.Name)) + (league.Teams.Count > 4 ? ", ..." : ""));
                Console.WriteLine("  spares   {0,2}: {1}", country.Spares.Count, string.Join(", ", country.Spares.Select(t => t.Club.Name)));
                Console.WriteLine("  tail     {0,2}: {1}, ...", country.TailClubs.Count, string.Join(", ", country.TailClubs.Take(5).Select(c => ClubNamer.ClubName(c))));
                var tooLong = country.AllTeams.Count(t => ClubNamer.ClubName(t.Club).Length > ClubNamer.MaxName);
                if (tooLong > 0) Console.WriteLine("  WARNING: {0} club names still too long", tooLong);
                var sizes = country.AllTeams.Select(t => t.Squad.Count).ToList();
                if (sizes.Count > 0) Console.WriteLine("  squads: min {0} max {1}", sizes.Min(), sizes.Max());
            }

            Console.WriteLine();
            Console.WriteLine("=== Avereine: {0} clubs, {1} extra clubs, nations: {2}", plan.Avereine.Teams.Count, plan.Avereine.ExtraClubs.Count,
                plan.Avereine.Teams.Select(t => t.Club.NationId).Distinct().Count());
            Console.WriteLine("  first: " + string.Join(", ", plan.Avereine.Teams.Take(6).Select(t => t.Club.Name)));
            var squadSizes = plan.Avereine.Teams.Select(t => t.Squad.Count).Distinct().OrderBy(x => x);
            Console.WriteLine("  squad sizes: " + string.Join(",", squadSizes));

            foreach (var w in plan.Warnings) Console.WriteLine("WARNING: " + w);

            // sample of converted players
            var laender = (LaenderFile)SavLoader.Load(Path.Combine(a2Dir, "DATA.DEF", "LAENDER.SAV"));
            var mapper = new PlayerMapper(settings, fm, new A2Nations(laender.Nations.Select(n => n.Name).ToList()));
            Console.WriteLine();
            Console.WriteLine("=== Sample players (first team of the first league of each country)");
            foreach (var country in plan.Countries)
            {
                var team = country.Tiers[0].Leagues[0].Teams.FirstOrDefault();
                if (team == null) continue;
                Console.WriteLine("  {0}:", team.Club.Name);
                foreach (var p in team.Squad.Take(4))
                {
                    var a = mapper.Map(p);
                    Console.WriteLine("    {0,-17}{1,-10} CA {2,3} -> str {3} age {4} pos {5}/{6}/{7} nat {8} +{9} -{10}",
                        a.LastName, a.FirstName, p.CurrentAbility, a.Strength, a.Age, a.Position1, a.Position2, a.Position3,
                        a.Nationality, a.PositiveSkills, a.NegativeSkills);
                }
            }

            if (args.Length > 2)
            {
                var outDir = args[2];
                Console.WriteLine();
                Console.WriteLine("=== Writing to " + outDir);
                var report = A2G.Convert.Writing.SavWriter.Write(plan, fm, settings, a2Dir, outDir);
                Console.WriteLine("  generated: " + string.Join(", ", report.FilesWritten.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)));
                foreach (var w in report.Warnings.Distinct()) Console.WriteLine("  WARNING: " + w);
                foreach (var n in report.Notes) Console.WriteLine("  NOTE: " + n);

                var errors = A2G.Convert.Writing.SavValidator.Validate(Path.Combine(outDir, "DATA.DEF"), Path.Combine(a2Dir, "DATA.DEF"));
                errors.AddRange(A2G.Convert.Writing.SavValidator.Validate(Path.Combine(outDir, "DATA.VER"), Path.Combine(a2Dir, "DATA.VER")));
                Console.WriteLine("  validation: {0} problem(s)", errors.Count);
                foreach (var e in errors.Take(25)) Console.WriteLine("    " + e);
                if (errors.Count > 0) return 2;
            }

            var strengths = plan.Countries.SelectMany(c => c.AllTeams).SelectMany(t => t.Squad).Select(p => mapper.Map(p).Strength)
                .GroupBy(x => x).OrderBy(g => g.Key).Select(g => g.Key + ":" + g.Count());
            Console.WriteLine();
            Console.WriteLine("Strength distribution (league + spare teams): " + string.Join("  ", strengths));
            var abilities = plan.Countries.SelectMany(c => c.AllTeams).SelectMany(t => t.Squad).Select(p => mapper.Map(p))
                .Count(a => a.PositiveSkills != 0);
            Console.WriteLine("Players with at least one ability: " + abilities);
            return 0;
        }
    }
}
