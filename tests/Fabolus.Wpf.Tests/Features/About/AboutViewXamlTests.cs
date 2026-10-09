using System.Windows.Controls;
using Fabolus.Wpf.Features.About;
using Xunit;

namespace Fabolus.Wpf.Tests.Features.About;

/// <summary>
/// Loads the About window for real, so a missing resource key fails here rather than the first
/// time someone opens it - which, for the window carrying the medical disclaimer, matters.
/// </summary>
public class AboutViewXamlTests
{
    [Fact]
    public void TheWindowLoadsAndShowsTheDisclaimer()
    {
        XamlHost.Run(() =>
        {
            var viewModel = new AboutViewModel();
            var view = new AboutView(viewModel);

            Assert.NotNull(view.Content);
            Assert.Same(viewModel, view.DataContext);
        });
    }
}
