using Fabolus.Core.Features.Moulds;
using Fabolus.Wpf.Features;
using Fabolus.Wpf.Features.AppPreferences;

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
) : IPreferenceSettings<MouldPreferences>
{
    public static string SectionKey => "mould";

    public static MouldPreferences Default { get; } = new(
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

    public static class Keys
    {
        public const string Shape = "mould_shape";
        public const string WallThickness = "mould_wall_thickness";
        public const string BaseHeight = "mould_base_height";
        public const string TroughHeight = "mould_trough_height";
        public const string TroughOffset = "mould_trough_offset";
        public const string TroughShape = "mould_trough_shape";
    }

    public static MouldPreferences Read(IPreferenceReader reader) => new(
        reader.GetEnum(Keys.Shape, "Mould shape", Default.Shape),
        reader.GetFloat(Keys.WallThickness, "Mould wall thickness", Default.WallThickness,
            Ranges.WallThicknessMin, Ranges.WallThicknessMax),
        reader.GetFloat(Keys.BaseHeight, "Mould base height", Default.BaseHeight,
            Ranges.BaseHeightMin, Ranges.BaseHeightMax),
        reader.GetFloat(Keys.TroughHeight, "Mould trough depth", Default.TroughHeight,
            Ranges.TroughHeightMin, Ranges.TroughHeightMax),
        reader.GetFloat(Keys.TroughOffset, "Mould trough margin", Default.TroughOffset,
            Ranges.TroughOffsetMin, Ranges.TroughOffsetMax),
        reader.GetEnum(Keys.TroughShape, "Mould trough shape", Default.TroughShape)
    );

    public void Write(IPreferenceWriter writer)
    {
        writer.SetEnum(Keys.Shape, Shape);
        writer.Set(Keys.WallThickness, WallThickness);
        writer.Set(Keys.BaseHeight, BaseHeight);
        writer.Set(Keys.TroughHeight, TroughHeight);
        writer.Set(Keys.TroughOffset, TroughOffset);
        writer.SetEnum(Keys.TroughShape, TroughShape);
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
