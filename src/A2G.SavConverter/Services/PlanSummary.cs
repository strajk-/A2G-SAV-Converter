using A2G.SavConverter.Localization;
using System.Linq;
using System.Text;
using A2G.Convert.Plan;

namespace A2G.SavConverter.Services
{
    /// <summary>Plain-text overview of a conversion plan for the Convert screen.</summary>
    public static class PlanSummary
    {
        public static string Build(ConversionPlan plan)
        {
            var sb = new StringBuilder();
            foreach (var country in plan.Countries)
            {
                sb.AppendLine(Loc.T(23000, country.Spec.Key, country.AllTeams.Count(), country.Spec.TeamCount));
                foreach (var tier in country.Tiers)
                    foreach (var league in tier.Leagues)
                        sb.AppendLine(string.Format("  {0,-24} {1,2}/{2,-2}  {3}", league.Spec.Name, league.Teams.Count, league.Spec.Size,
                            string.Join(", ", league.Teams.Take(4).Select(t => t.Club.Name)) + (league.Teams.Count > 4 ? ", ..." : "")));
                sb.AppendLine(string.Format("  {0,-24} {1,2}    {2}", Loc.T(23001), country.Spares.Count,
                    string.Join(", ", country.Spares.Take(4).Select(t => t.Club.Name)) + (country.Spares.Count > 4 ? ", ..." : "")));
                sb.AppendLine(string.Format("  {0,-24} {1,2}", Loc.T(23002), country.TailClubs.Count));
                sb.AppendLine();
            }
            sb.AppendLine(Loc.T(23003, plan.Avereine.Teams.Count, plan.Avereine.Teams.Select(t => t.Club.NationId).Distinct().Count(), plan.Avereine.ExtraClubs.Count));
            // stadium sizes: how many clubs have the ground of the FM save (the others are estimated from their reputation)
            var planClubs = plan.Countries.SelectMany(c => c.AllTeams).Concat(plan.Avereine.Teams).Select(t => t.Club).ToList();
            var withGround = planClubs.Count(c => c.StadiumCapacity > 0);
            if (withGround > 0) sb.AppendLine(Loc.T(28084, withGround, planClubs.Count));
            if (plan.Warnings.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine(Loc.T(23004));
                foreach (var w in plan.Warnings) sb.AppendLine("  " + w);
            }
            return sb.ToString();
        }
    }
}
