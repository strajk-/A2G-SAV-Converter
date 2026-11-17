using System.Collections.Generic;
using System.Linq;
using A2G.Convert.Engine;
using A2G.SavConverter.Localization;

namespace A2G.SavConverter.Services
{
    /// <summary>The texts of the problems the plan validator finds, in the current language.</summary>
    public static class PlanProblemText
    {
        public static string Format(PlanProblem p)
        {
            switch (p.Kind)
            {
                case PlanProblemKind.EmptySlot:
                    return Loc.T(28160, p.Country, p.League, SeasonResultsWindow.SlotLabel(p.Slot));
                case PlanProblemKind.DuplicateSlot:
                    return Loc.T(28161, p.Country, p.League, p.Club, SeasonResultsWindow.SlotLabel(p.Slot), SeasonResultsWindow.SlotLabel(p.Slot2));
                case PlanProblemKind.CupPlaceLost:
                    return Loc.T(28162, p.Country, p.League);
                default:
                    return Loc.T(28163, Loc.T(28143 + p.Competition));
            }
        }

        public static List<string> Format(IEnumerable<PlanProblem> problems)
        {
            return problems.Select(Format).ToList();
        }
    }
}
