using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace A2G.SavConverter.Services
{
    internal static class UiHelpers
    {
        /// <summary>True when the clicked element sits inside an item container of type <typeparamref name="T"/> (a click on empty list space does not).</summary>
        public static bool IsOnItem<T>(object originalSource) where T : DependencyObject
        {
            var d = originalSource as DependencyObject;
            while (d != null)
            {
                if (d is T) return true;
                d = d is Visual || d is Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            }
            return false;
        }
    }
}
