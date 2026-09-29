using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Fabolus.Wpf.Features.Smoothing;
using Xunit;

namespace Fabolus.Wpf.Tests.Features.Smoothing;

/// <summary>
/// The Display segmented control in the smoothing rail, laid out for real.
/// </summary>
/// <remarks>
/// The three segments used to run edge to edge: the template padded them vertically only, so the
/// labels touched and the selected segment's pill was drawn hard against its neighbours. The
/// labels were also wider than the column they sat in - "Cross Section" is about as wide as the
/// whole third of the rail it had to fit inside.
///
/// This measures the control the way WPF will, at both ends of the rail's resize range, rather
/// than trusting the numbers in the XAML comment to stay true.
/// </remarks>
public class SmoothingDisplaySegmentsTests
{
    // The rail column is 280 wide by default and can be dragged down to 240.
    private static void OnStaThread(Action action) => XamlHost.Run(action);

    [Theory]
    [InlineData(280.0)]   // the rail's default width
    [InlineData(240.0)]   // and the narrowest it can be dragged to
    public void EverySegmentLabelFitsInsideItsSegment(double railWidth) =>
        OnStaThread(() =>
        {
            foreach (var segment in DisplaySegments(railWidth))
            {
                // Not DesiredSize: Measure clamps that to the width it was offered, so a label
                // too wide for its column reports a desired width equal to the column and the
                // comparison is always true. The text has to be measured unconstrained instead.
                var text = NaturalTextWidth(segment);
                var available = segment.ActualWidth - segment.Padding.Left - segment.Padding.Right;

                Assert.True(
                    available + 0.5 >= text,
                    $"'{segment.Content}' needs {text:F1}px of text but has {available:F1}px "
                        + $"between its padding at a {railWidth}px rail.");
            }
        });

    /// <summary>How wide the segment's label wants to be with nothing constraining it.</summary>
    private static double NaturalTextWidth(RadioButton segment)
    {
        var probe = new TextBlock
        {
            Text = (string)segment.Content,
            FontFamily = segment.FontFamily,
            FontSize = segment.FontSize,
            FontWeight = FontWeights.SemiBold,   // the weight the selected segment switches to
            FontStyle = segment.FontStyle,
        };

        probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return probe.DesiredSize.Width;
    }

    [Theory]
    [InlineData(280.0)]
    [InlineData(240.0)]
    public void AdjacentSegmentsDoNotTouch(double railWidth) =>
        OnStaThread(() =>
        {
            var (view, segments) = DisplayControl(railWidth);

            var bounds = segments
                .Select(s => RenderedBounds(s, view))
                .OrderBy(b => b.Left)
                .ToList();

            foreach (var (left, right) in bounds.Zip(bounds.Skip(1)))
            {
                var gap = right.Left - left.Right;
                Assert.True(gap >= 2.0,
                    $"only {gap:F1}px between segments at a {railWidth}px rail - they run together.");
            }
        });

    /// <summary>
    /// The labels are short on purpose: three full names will not fit across the rail. Their
    /// meaning lives in the tooltips instead, so every segment has to carry one.
    /// </summary>
    [Fact]
    public void EverySegmentHasATooltip() =>
        OnStaThread(() =>
        {
            foreach (var segment in DisplaySegments(280.0))
            {
                Assert.False(string.IsNullOrWhiteSpace(segment.ToolTip as string),
                    $"'{segment.Content}' has no tooltip to spell it out.");
            }
        });

    private static List<RadioButton> DisplaySegments(double railWidth) =>
        DisplayControl(railWidth).Segments;

    /// <summary>
    /// The smoothing view measured and arranged inside a rail of the given width, with its three
    /// Display radio buttons.
    /// </summary>
    /// <remarks>
    /// No DataContext: the IsChecked bindings simply go unresolved, which does not affect layout,
    /// and building a real view model would drag the geometry engine in for nothing.
    /// </remarks>
    private static (SmoothingView View, List<RadioButton> Segments) DisplayControl(double railWidth)
    {
        var view = new SmoothingView();

        view.Measure(new Size(railWidth, 2000));
        view.Arrange(new Rect(0, 0, railWidth, 2000));
        view.UpdateLayout();

        var segments = Descendants(view)
            .OfType<RadioButton>()
            .Where(r => r.GroupName == "DisplayMode")
            .ToList();

        Assert.Equal(3, segments.Count);
        return (view, segments);
    }

    private static Rect RenderedBounds(FrameworkElement element, Visual ancestor)
    {
        var offset = element.TransformToAncestor(ancestor).Transform(new Point(0, 0));
        return new Rect(offset, new Size(element.ActualWidth, element.ActualHeight));
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;

            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

}
