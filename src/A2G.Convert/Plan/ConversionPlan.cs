using System.Collections.Generic;
using System.Linq;
using A2G.Convert.Fm;

namespace A2G.Convert.Plan
{
    /// <summary>A club placed in a slot together with the players chosen for it.</summary>
    public sealed class TeamPlan
    {
        public TeamPlan(FmClub club)
        {
            Club = club;
            Squad = new List<FmPlayer>();
        }
        public FmClub Club { get; private set; }
        public List<FmPlayer> Squad { get; private set; }
    }

    public sealed class LeaguePlan
    {
        public LeaguePlan(A2LeagueSpec spec)
        {
            Spec = spec;
            Teams = new List<TeamPlan>();
        }
        public A2LeagueSpec Spec { get; private set; }
        public List<TeamPlan> Teams { get; private set; }
    }

    public sealed class TierPlan
    {
        public TierPlan(A2TierSpec spec)
        {
            Spec = spec;
            Leagues = spec.Leagues.Select(l => new LeaguePlan(l)).ToList();
        }
        public A2TierSpec Spec { get; private set; }
        public List<LeaguePlan> Leagues { get; private set; }
    }

    /// <summary>The content of one Land*.sav (plus the matching Stad*.sav and LIGA*.SAV files).</summary>
    public sealed class CountryPlan
    {
        public CountryPlan(A2CountrySpec spec)
        {
            Spec = spec;
            Tiers = spec.Tiers.Select(t => new TierPlan(t)).ToList();
            Spares = new List<TeamPlan>();
            TailClubs = new List<FmClub>();
        }
        public A2CountrySpec Spec { get; private set; }
        public List<TierPlan> Tiers { get; private set; }
        public List<TeamPlan> Spares { get; private set; }
        /// <summary>Clubs whose names fill the spare club name list at the end of the Land file.</summary>
        public List<FmClub> TailClubs { get; private set; }

        public IEnumerable<TeamPlan> AllTeams
        {
            get { return Tiers.SelectMany(t => t.Leagues).SelectMany(l => l.Teams).Concat(Spares); }
        }
    }

    /// <summary>The content of Avereine.sav.</summary>
    public sealed class AvereinePlan
    {
        public AvereinePlan()
        {
            Teams = new List<TeamPlan>();
            ExtraClubs = new List<FmClub>();
        }
        public List<TeamPlan> Teams { get; private set; }
        public List<FmClub> ExtraClubs { get; private set; }
    }

    /// <summary>
    /// What the engine decided; the editor UI changes this before the .sav files are written from it.
    /// </summary>
    public sealed class ConversionPlan
    {
        public ConversionPlan()
        {
            Countries = new List<CountryPlan>();
            Avereine = new AvereinePlan();
            Warnings = new List<string>();
        }
        public List<CountryPlan> Countries { get; private set; }
        public AvereinePlan Avereine { get; private set; }
        public List<string> Warnings { get; private set; }
    }
}
