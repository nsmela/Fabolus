using System.Windows;
using System.Windows.Controls;

namespace Fabolus.Wpf.Features.AppPreferences;

/// <summary>
/// Picks the markup for a preference row.
///
/// Only <see cref="CustomRow"/> needs deciding - it names the template it wants, which is looked
/// up from the row's own place in the tree so the page's window resources are in scope. Every
/// other row type returns null and falls through to the implicit DataType templates.
/// </summary>
public sealed class PreferenceRowTemplateSelector : DataTemplateSelector {
    public override DataTemplate? SelectTemplate(object? item, DependencyObject container) {
        if (item is CustomRow custom && container is FrameworkElement element) {
            return element.TryFindResource(custom.TemplateKey) as DataTemplate;
        }

        return null;
    }
}
