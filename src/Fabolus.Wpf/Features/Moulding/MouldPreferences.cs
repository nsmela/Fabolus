using Fabolus.Core.Features.Moulds;

namespace Fabolus.Wpf.Features.Moulding;

/// <summary>
/// User preferences for mould generation.
/// </summary>
public sealed record MouldPreferences(
    MouldShapeType Shape,
    float WallThickness,
    float BaseHeight,
    float TroughHeight,
    float TroughOffset,
    TroughShapeType TroughShape
) : IPreferenceSettings
{
    public static readonly MouldPreferences Default = new(
        Shape: MouldShapeType.Concave,
        WallThickness: 2.0f,
        BaseHeight: 5.0f,
        TroughHeight: 0.0f,
        TroughOffset: 2.5f,
        TroughShape: TroughShapeType.Footprint
    );

    public static class Ranges
    {
        public const float WallThicknessMin = 1.0f;
        public const float WallThicknessMax = 10.0f;
        public const float BaseHeightMin = 2.0f;
        public const float BaseHeightMax = 20.0f;
        public const float TroughHeightMin = 0.0f;
        public const float TroughHeightMax = 10.0f;
        public const float TroughOffsetMin = 1.0f;
        public const float TroughOffsetMax = 10.0f;
    }

    public MouldDefinition ToMouldDefinition()
    {
        MouldDefinition definition = Shape switch
        {
            MouldShapeType.Convex => new ConvexMouldDefinition(WallThickness, BaseHeight, BaseHeight),
            MouldShapeType.Contoured => new ContouredMouldDefinition(WallThickness),
            _ => new ConcaveMouldDefinition(WallThickness, BaseHeight, BaseHeight)
        };

        return definition with
        {
            TroughHeight = TroughHeight,
            TroughOffset = TroughOffset,
            TroughShape = TroughShape
        };
    }

    public MouldPreferences Clamped() => new(
        Enum.IsDefined(Shape) ? Shape : Default.Shape,
        Math.Clamp(WallThickness, Ranges.WallThicknessMin, Ranges.WallThicknessMax),
        Math.Clamp(BaseHeight, Ranges.BaseHeightMin, Ranges.BaseHeightMax),
        Math.Clamp(TroughHeight, Ranges.TroughHeightMin, Ranges.TroughHeightMax),
        Math.Clamp(TroughOffset, Ranges.TroughOffsetMin, Ranges.TroughOffsetMax),
        Enum.IsDefined(TroughShape) ? TroughShape : Default.TroughShape
    );
}
