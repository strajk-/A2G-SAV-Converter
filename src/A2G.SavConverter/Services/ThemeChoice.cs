using A2G.SavConverter.Localization;

namespace A2G.SavConverter.Services
{
    /// <summary>An entry of the theme drop-down (System / Light / Dark) shown in the current language; used by the converter and the patcher.</summary>
    internal sealed class ThemeChoice
    {
        public ThemeMode Mode;
        public override string ToString() { return Loc.T(Mode == ThemeMode.System ? 20050 : Mode == ThemeMode.Light ? 20051 : 20052); }
    }
}
