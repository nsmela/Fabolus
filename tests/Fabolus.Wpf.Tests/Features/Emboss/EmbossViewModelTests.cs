using System.Numerics;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Common;
using Fabolus.Core.Common.Interfaces;
using Fabolus.Core.Features.Emboss;
using Fabolus.Core.Geometry;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Features.AppPreferences;
using Fabolus.Wpf.Features.Emboss;
using Moq;
using Xunit;

namespace Fabolus.Wpf.Tests.Features.Emboss;

public sealed class TestOutlineSource : IGlyphOutlineSource
{
    public IReadOnlyList<Polygon2D> GetOutlines(string text, DecalFont font, float capHeight, float tracking)
    {
        return new List<Polygon2D>
        {
            new()
            {
                OuterBoundary = new List<Vector2>
                {
                    new(-5, -3), new(5, -3), new(5, 3), new(-5, 3)
                }
            }
        };
    }

    public TextMetrics MeasureText(string text, DecalFont font, float capHeight, float tracking)
    {
        return new TextMetrics(10f, capHeight, new float[] { 10f });
    }
}

public class EmbossViewModelTests
{
    private static (EmbossViewModel vm, IMessenger messenger, Mock<IGeometryEngine> engineMock) CreateViewModel()
    {
        var messenger = new StrongReferenceMessenger();
        var preferences = new Dictionary<string, object>
        {
            [UISettings.PrintBedWidthLabel] = 250.0f,
            [UISettings.PrintBedDepthLabel] = 250.0f,
            [UISettings.ShowBedGridLabel] = true,
        };

        messenger.Register<Dictionary<string, object>, AppPreferenceRequestMessage>(preferences, (r, m) =>
        {
            if (r.TryGetValue(m.Key, out var val))
                m.Reply(val);
        });

        var engineMock = new Mock<IGeometryEngine>();
        var alertMock = new Mock<IAlertDialog>();
        var outlineSource = new TestOutlineSource();

        var vm = new EmbossViewModel(messenger, alertMock.Object, engineMock.Object, outlineSource);
        return (vm, messenger, engineMock);
    }

    [Fact]
    public void Target_ChangingToMould_SetsMirrorTrue()
    {
        var (vm, _, _) = CreateViewModel();

        vm.Target = EmbossTarget.Base;
        Assert.False(vm.Mirror);

        vm.Target = EmbossTarget.Mould;
        Assert.True(vm.Mirror);

        vm.Target = EmbossTarget.Base;
        Assert.False(vm.Mirror);
    }

    [Fact]
    public void Operation_ChangingToEngrave_UpdatesDepthLabelAndApplyLabel()
    {
        var (vm, _, _) = CreateViewModel();

        vm.Operation = EmbossOperation.Emboss;
        Assert.Equal("Height", vm.DepthLabel);
        Assert.Equal("Apply emboss", vm.ApplyLabel);

        vm.Operation = EmbossOperation.Engrave;
        Assert.Equal("Depth", vm.DepthLabel);
        Assert.Equal("Apply engraving", vm.ApplyLabel);
    }

    [Fact]
    public void StartPlacingCommand_TogglesIsPicking()
    {
        var (vm, _, _) = CreateViewModel();

        Assert.False(vm.IsPicking);

        vm.StartPlacingCommand.Execute(null);
        Assert.True(vm.IsPicking);

        vm.StartPlacingCommand.Execute(null);
        Assert.False(vm.IsPicking);
    }

    [Fact]
    public void ResetCommand_ResetsRotation()
    {
        var (vm, _, _) = CreateViewModel();

        vm.Rotation = 45;
        vm.ResetCommand.Execute(null);

        Assert.Equal(0, vm.Rotation);
    }
}
