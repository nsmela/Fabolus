using Fabolus.Core.Geometry;
using HelixToolkit.Wpf.SharpDX;
using SharpDX;
using MediaColor = System.Windows.Media.Color;
using CoreVector3 = System.Numerics.Vector3;
// Media3D carries a Material of its own, which is not the one Helix's models take - so the transform
// types are named rather than the namespace imported.
using Transform3D = System.Windows.Media.Media3D.Transform3D;
using Transform3DGroup = System.Windows.Media.Media3D.Transform3DGroup;
using ScaleTransform3D = System.Windows.Media.Media3D.ScaleTransform3D;
using TranslateTransform3D = System.Windows.Media.Media3D.TranslateTransform3D;

namespace Fabolus.Wpf.Features.PartingSplit;

/// <summary>Which part of the sectioned line a click landed on.</summary>
/// <param name="Rim">Which rim, for a body that parts along more than one.</param>
/// <param name="Index">The section, or the handle, depending on <paramref name="IsHandle"/>.</param>
public readonly record struct PartingLinePick(int Rim, int Index, bool IsHandle);

/// <summary>
/// The visuals for editing a parting line by hand: one line model per section and one cube per
/// handle, each carrying enough identity to say what was clicked.
///
/// <para>
/// Split out of the scene manager because it is a different kind of thing from the rest of that class.
/// Everything else there draws a result; this draws a set of controls, and a control has to be
/// hit-testable, has to know which of many it is, and has to look different when it is pointed at or
/// selected. Held together here so the mapping from model to section lives in one place - the failure
/// this avoids is a stale dictionary handing back the index of a section that has since been merged
/// away.
/// </para>
///
/// <para>
/// The controls are built once and then <em>mutated</em>. That is the whole shape of this class and it
/// is worth stating plainly: a rebuild makes new models with new ids, so every rebuild is a removal and
/// an addition through the scene manager's events - a dispatcher round-trip, a Dispose and a
/// Viewport.Items edit per model. That is affordable when the user adds a handle. It is not affordable
/// on every mouse-move, and hover and dragging are both mouse-move events. So appearance
/// (<see cref="ApplyState"/>) and small geometry changes (<see cref="TryUpdate"/>) are written straight
/// onto the live models, and <see cref="Build"/> is reserved for the case where the number of controls
/// has actually changed and there is no longer anything to write onto.
/// </para>
/// </summary>
internal sealed class PartingLineEditVisuals
{
    /// <summary>
    /// How wide a section is to the mouse, in pixels either side.
    ///
    /// <para>
    /// Was fourteen, chosen when a click on a section only selected it: a curve on a curved surface seen
    /// in perspective presents a couple of pixels to aim at however thickly it is drawn, so the target
    /// was widened rather than the drawing. A click on a section now <em>divides</em> it, and that
    /// changes which way the trade runs. A twenty-eight pixel band around a line that also carries the
    /// handles means every near-miss on a handle lands on the line instead, and lands there as an edit -
    /// the user reaches for a cube, misses by three pixels, and gets a breakpoint they did not ask for.
    /// A missed click that does nothing is a far cheaper failure than a hit that changes the line.
    /// </para>
    /// </summary>
    private const double SectionHitTolerance = 6.0;

    /// <summary>
    /// How much larger than the cube its target for the mouse is.
    ///
    /// <para>
    /// This is the same separation <see cref="SectionHitTolerance"/> gives the lines, applied to the
    /// handles: how big a control looks and how big it is to aim at are different questions, and tying
    /// them together means answering one badly. The cube's size is chosen to sit legibly on a band a few
    /// millimetres wide - see <see cref="HandleRadiusFraction"/> - and at that size it is one or two
    /// millimetres across, which at any normal zoom is a handful of pixels to hit. So it is aimed at
    /// through a box two and a half times its width that is never drawn.
    /// </para>
    ///
    /// <para>
    /// Comfortably wider than <see cref="SelectedGrowth"/>, so the target still contains the cube when
    /// the cube is at its largest.
    /// </para>
    /// </summary>
    private const float HandleHitScale = 2.5f;

    /// <summary>
    /// Half a handle's width at rest, as a fraction of the rim wall's own width - so a cube is
    /// <c>2 x</c> this across. Sized against the wall rather than the body: see Build.
    ///
    /// <para>
    /// Small on purpose. At the 0.42 this started at, a cube came out 0.84 of the wall across - very
    /// nearly as wide as the band it sits on, so the handles hid the thing being adjusted and two
    /// neighbouring ones touched. At 0.10 a cube is a fifth of the wall: about 2.3mm on the thickest
    /// body in the set and 1.0mm on the thinnest, which reads as a marker on the line rather than as
    /// an object covering it.
    /// </para>
    /// </summary>
    private const float HandleRadiusFraction = 0.10f;

    /// <summary>
    /// What a cube swells to when the cursor is over it, and when it is selected.
    ///
    /// <para>
    /// Hover is deliberately slight. It is answering "the click will land here", and it has to say that
    /// about a cube a fifth of the wall wide without covering the neighbouring wall the user is about to
    /// drag onto - so it is a nudge, not an announcement. Selection may be louder because it persists
    /// and because it also changes colour, which is the part that carries the meaning.
    /// </para>
    /// </summary>
    private const float HoverGrowth = 1.25f;
    private const float SelectedGrowth = 1.45f;

    /// <summary>
    /// What the marker showing where a new handle would go is sized at, against a real one.
    ///
    /// <para>
    /// Smaller, so it never reads as a handle that is already there. It is a proposal, and the thing it
    /// has to say is where the click will land rather than what will exist afterwards.
    /// </para>
    /// </summary>
    private const float PreviewScale = 0.8f;

    private static readonly MediaColor SoundSection = MediaColor.FromRgb(255, 226, 40);
    private static readonly MediaColor FaultySection = MediaColor.FromRgb(255, 130, 60);
    private static readonly MediaColor SelectedSection = MediaColor.FromRgb(90, 255, 150);

    /// <summary>
    /// The section under the cursor, which is the one a click divides. Brightened towards white rather
    /// than given a colour of its own: a section already carries a colour that means something - sound
    /// or faulty - and replacing it on hover would hide the reading at the moment the user is deciding
    /// where to cut.
    /// </summary>
    private static readonly MediaColor HoveredSection = MediaColor.FromRgb(255, 250, 210);

    // Blue at rest, paler blue under the cursor, amber when selected.
    //
    // Amber rather than the section's own green, which is what a selected handle used to take. A
    // selecting a handle also selects nothing else, so the green cube sat on a green line and the one
    // control the user had just taken hold of was the hardest one on screen to find. Amber is the
    // furthest thing from the resting blue that none of the line colours already occupy.
    private static readonly MediaColor HandleColour = MediaColor.FromRgb(70, 200, 255);
    private static readonly MediaColor HoveredHandleColour = MediaColor.FromRgb(175, 235, 255);
    private static readonly MediaColor SelectedHandleColour = MediaColor.FromRgb(255, 178, 40);

    // Shared across every handle. The scene has no lights, so unlit DiffuseMaterials are used - a
    // PhongMaterial would render black.
    private static readonly Material HandleSkin = SkinOf(HandleColour);
    private static readonly Material HoveredHandleSkin = SkinOf(HoveredHandleColour);
    private static readonly Material SelectedHandleSkin = SkinOf(SelectedHandleColour);

    /// <summary>Green, the colour of the thing that is about to be added rather than of one that is.</summary>
    private static readonly Material PreviewSkin = SkinOf(MediaColor.FromRgb(120, 255, 170));

    /// <summary>
    /// Draws nothing. Fully transparent rather than hidden, because hiding a model takes it out of the
    /// hit test as well - and being in the hit test while contributing no pixels is the whole job.
    /// </summary>
    private static readonly Material InvisibleSkin =
        new DiffuseMaterial { DiffuseColor = new Color4(0f, 0f, 0f, 0f) };

    /// <summary>
    /// One handle: the cube that is drawn, the larger box it is aimed at through, which of many it is,
    /// and where on the wall it sits.
    /// </summary>
    private sealed class HandleVisual
    {
        public required MeshGeometryModel3D Model { get; init; }

        /// <summary>The target for the mouse - never drawn, and the only one of the two hit-tested.</summary>
        public required MeshGeometryModel3D Target { get; init; }

        public required int Rim { get; init; }
        public required int Index { get; init; }
        public CoreVector3 Position { get; set; }
    }

    /// <summary>
    /// One section: the curve, which of many it is, and the colour its condition earns it - kept
    /// alongside so selection can be applied and lifted without re-reading the edit.
    /// </summary>
    private sealed class SectionVisual
    {
        public required LineGeometryModel3D Model { get; init; }
        public required int Rim { get; init; }
        public required int Index { get; init; }
        public MediaColor Resting { get; set; }

        /// <summary>
        /// The span this model's geometry was built from, so an update can skip the sections that did
        /// not move. An edit re-walks the one or two spans it touches and carries the rest across by
        /// reference, so this identifies the untouched ones exactly rather than by comparing points.
        /// </summary>
        public PartingSpan? Source { get; set; }
    }

    private readonly Dictionary<Guid, PartingLinePick> _picks = new();
    private readonly List<Element3D> _models = new();
    private readonly List<HandleVisual> _handles = new();
    private readonly List<SectionVisual> _sections = new();

    /// <summary>
    /// The marker showing where a click would put a handle, or null when the cursor is not over a
    /// section. Built with everything else and then only shown and moved, for the same reason the
    /// handles are: it follows the cursor, so it cannot afford to be rebuilt.
    /// </summary>
    private MeshGeometryModel3D? _preview;
    private CoreVector3? _previewAt;

    /// <summary>Half a handle's width at rest, in mm. Set from the wall on each build.</summary>
    private float _radius = 0.2f;

    private bool _hitTestable = true;

    public IReadOnlyList<Element3D> Models => _models;

    public IEnumerable<Guid> Ids => _models.Select(m => m.GUID);

    /// <summary>What a clicked model is, or null if it is not part of the editing controls.</summary>
    public PartingLinePick? Identify(Element3D? model) =>
        model is not null && _picks.TryGetValue(model.GUID, out var pick) ? pick : null;

    /// <summary>
    /// Whether the controls take part in the hit test at all.
    ///
    /// <para>
    /// Switched off for the duration of a drag, and this is what makes dragging work. The viewport hands
    /// the scene manager its nearest hit, and the controls are all nearer the camera than the body they
    /// lie on: the cube is a solid standing proud of the surface, and a section is a line with fourteen
    /// pixels of hit tolerance either side. So while a handle is being dragged along the line, the ray
    /// keeps striking the neighbouring sections and cubes, and the point reported back is on one of
    /// those rather than on the wall the user is pointing at. Suppressing the dragged handle alone - as
    /// this did at first - only fixes the case where the cursor has not yet moved.
    /// </para>
    /// </summary>
    public bool IsHitTestable
    {
        get => _hitTestable;
        set
        {
            if (_hitTestable == value) return;

            _hitTestable = value;

            // Driven off the two collections that hold the actual targets rather than off every model,
            // so the ones that must never be hit-tested cannot be switched on by accident: the drawn
            // cube, which its own target already covers, and the marker, which lies on the line it is
            // proposing to divide and must not take that line's click.
            foreach (var section in _sections) section.Model.IsHitTestVisible = value;
            foreach (var handle in _handles) handle.Target.IsHitTestVisible = value;
        }
    }

    public void Clear()
    {
        _picks.Clear();
        _models.Clear();
        _handles.Clear();
        _sections.Clear();
        _preview = null;
        _previewAt = null;
    }

    /// <summary>
    /// Shows the marker at a point on the line, or takes it away. Null whenever a click would not add a
    /// handle - off the line, or too near one that is already there - so the marker's absence is the
    /// editor saying there is nothing to divide, rather than the click silently doing nothing.
    /// </summary>
    public void SetPreview(CoreVector3? at)
    {
        _previewAt = at;
        ApplyPreview();
    }

    /// <summary>
    /// Puts the marker back where the preview state says it should be. Called after any pass that sets
    /// visibility across every control, since the marker's visibility is not the state's to decide.
    /// </summary>
    public void ApplyPreview()
    {
        if (_preview is null) return;

        bool shown = _previewAt is not null;
        _preview.Visibility = shown
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;
        _preview.IsRendering = shown;

        if (_previewAt is { } at)
            _preview.Transform = TransformOf(at, _radius * 2f * PreviewScale);
    }

    /// <summary>
    /// Rebuilds every control from the edit as it now stands.
    ///
    /// <para>
    /// Rebuilt wholesale rather than patched whenever the shape of the edit changes. An edit changes how
    /// many sections there are as often as not - removing a handle merges two into one, adding one
    /// splits one into two - so there is no stable identity to patch against, and a partial update is how
    /// a handle comes to be left behind pointing at a section that no longer exists. Where the shape has
    /// <em>not</em> changed, <see cref="TryUpdate"/> patches instead; see the note on this class.
    /// </para>
    /// </summary>
    /// <param name="handleSize">
    /// The wall's width, which is what the handles are scaled from. Sized against the wall rather than
    /// the body because that is what the user is aiming inside: a handle bigger than the band it sits
    /// on hides the thing being adjusted, and one scaled to a large body would do exactly that on a
    /// small rim.
    /// </param>
    public void Build(
        PartingLineEdit? edit, float handleSize,
        PartingLinePick? selected, PartingLinePick? hovered)
    {
        Clear();
        if (edit is null || edit.IsEmpty) return;

        _radius = MathF.Max(handleSize * HandleRadiusFraction, 0.2f);

        for (int rim = 0; rim < edit.Rims.Count; rim++)
        {
            var line = edit.Rims[rim].Line;

            for (int s = 0; s < line.Spans.Count; s++)
            {
                var span = line.Spans[s];
                if (span.Points.Count < 2) continue;

                var model = new LineGeometryModel3D
                {
                    Geometry = GeometryOf(span),
                    IsHitTestVisible = _hitTestable,
                    HitTestThickness = SectionHitTolerance,
                };

                _sections.Add(new SectionVisual
                {
                    Model = model,
                    Rim = rim,
                    Index = s,
                    Resting = RestingColour(span),
                    Source = span,
                });

                _picks[model.GUID] = new PartingLinePick(rim, s, IsHandle: false);
                _models.Add(model);
            }

            for (int a = 0; a < line.Anchors.Count; a++)
            {
                // A cube rather than a sphere: its silhouette is the same from every angle a user is
                // likely to be at, so a handle reads as the same size wherever it sits round the rim,
                // and its flat faces catch the light differently from the body it lies on.
                //
                // Built at the origin one unit across and put in place by its transform, so growing it
                // under the cursor and moving it under a drag are both a transform swap rather than a
                // new MeshGeometry3D. That is what lets hover be answered on a mouse-move.
                var builder = new MeshBuilder();
                builder.AddBox(Vector3.Zero, 1f, 1f, 1f);

                // Not hit-tested itself. The target below sits around it and is nearer the camera at
                // every pixel the cube covers, so leaving both in the hit test would only put two
                // answers to the same question into the list.
                var model = new MeshGeometryModel3D
                {
                    Geometry = builder.ToMeshGeometry3D(),
                    Material = HandleSkin,
                    IsHitTestVisible = false,
                };

                var targetBuilder = new MeshBuilder();
                targetBuilder.AddBox(Vector3.Zero, 1f, 1f, 1f);

                var target = new MeshGeometryModel3D
                {
                    Geometry = targetBuilder.ToMeshGeometry3D(),
                    Material = InvisibleSkin,
                    IsTransparent = true,
                    IsHitTestVisible = _hitTestable,
                };

                _handles.Add(new HandleVisual
                {
                    Model = model,
                    Target = target,
                    Rim = rim,
                    Index = a,
                    Position = line.Anchors[a].Position,
                });

                _picks[target.GUID] = new PartingLinePick(rim, a, IsHandle: true);
                _models.Add(model);
                _models.Add(target);
            }
        }

        // Deliberately out of the hit test, and out of _picks. It sits on the line it is proposing to
        // divide, so left in it would take the click meant for that line and identify as nothing.
        var preview = new MeshBuilder();
        preview.AddBox(Vector3.Zero, 1f, 1f, 1f);

        _preview = new MeshGeometryModel3D
        {
            Geometry = preview.ToMeshGeometry3D(),
            Material = PreviewSkin,
            IsHitTestVisible = false,
        };

        _models.Add(_preview);

        ApplyState(selected, hovered);
        ApplyPreview();
    }

    /// <summary>
    /// Writes an edit onto the controls already on screen, or reports that it cannot.
    ///
    /// <para>
    /// It cannot whenever the edit has a different number of anchors or spans than the controls were
    /// built for, because then there is no correspondence to write along - and the caller rebuilds. It
    /// can for every frame of a drag, which is the case that matters: a drag re-walks the two spans
    /// meeting at the handle and leaves the rest of the line alone, so what actually has to reach the
    /// screen is two curves and one transform.
    /// </para>
    /// </summary>
    /// <returns>Whether the controls now show <paramref name="edit"/>.</returns>
    public bool TryUpdate(
        PartingLineEdit? edit, float handleSize,
        PartingLinePick? selected, PartingLinePick? hovered)
    {
        if (edit is null || edit.IsEmpty || _models.Count == 0) return false;
        if (!Matches(edit)) return false;

        _radius = MathF.Max(handleSize * HandleRadiusFraction, 0.2f);

        foreach (var section in _sections)
        {
            var span = edit.Rims[section.Rim].Line.Spans[section.Index];

            // Carried across by reference by every edit that did not touch it, so this skips rebuilding
            // the geometry of every section the user is not dragging.
            if (ReferenceEquals(section.Source, span)) continue;

            section.Model.Geometry = GeometryOf(span);
            section.Resting = RestingColour(span);
            section.Source = span;
        }

        foreach (var handle in _handles)
            handle.Position = edit.Rims[handle.Rim].Line.Anchors[handle.Index].Position;

        ApplyState(selected, hovered);
        ApplyPreview();
        return true;
    }

    /// <summary>
    /// Puts every control into the appearance its selection and hover state ask for. Written onto the
    /// live models, so this is what hover costs: a material reference and a transform per handle.
    /// </summary>
    public void ApplyState(PartingLinePick? selected, PartingLinePick? hovered)
    {
        foreach (var section in _sections)
        {
            bool isSelected = selected is { IsHandle: false } pick
                && pick.Rim == section.Rim && pick.Index == section.Index;
            bool isHovered = hovered is { IsHandle: false } under
                && under.Rim == section.Rim && under.Index == section.Index;

            // Hover outranks selection here, the opposite way round from the handles. A handle's colour
            // says which one is selected and has to keep saying it; a section's says which one is about
            // to be divided, and that is the more urgent of the two while the cursor is on it.
            section.Model.Color = isHovered ? HoveredSection
                : isSelected ? SelectedSection
                : section.Resting;

            section.Model.Thickness = isHovered || isSelected ? 7.5 : 4.5;
        }

        foreach (var handle in _handles)
        {
            bool isSelected = selected is { IsHandle: true } chosen
                && chosen.Rim == handle.Rim && chosen.Index == handle.Index;
            bool isHovered = hovered is { IsHandle: true } under
                && under.Rim == handle.Rim && under.Index == handle.Index;

            // Selection outranks hover: a selected handle under the cursor stays amber rather than
            // reverting to the pale blue that means "not this one yet".
            handle.Model.Material = isSelected ? SelectedHandleSkin
                : isHovered ? HoveredHandleSkin
                : HandleSkin;

            float growth = isSelected ? SelectedGrowth : isHovered ? HoverGrowth : 1f;
            handle.Model.Transform = TransformOf(handle.Position, _radius * 2f * growth);

            // The target does not grow with it. Its size is what makes the handle easy to aim at, and
            // that should not depend on whether the handle is already selected - a control that is
            // harder to hit until you have hit it once is the wrong way round.
            handle.Target.Transform = TransformOf(handle.Position, _radius * 2f * HandleHitScale);
        }
    }

    /// <summary>Whether the edit has exactly the controls this already holds, section for section.</summary>
    private bool Matches(PartingLineEdit edit)
    {
        int sections = 0, handles = 0;

        foreach (var section in _sections)
        {
            if (section.Rim >= edit.Rims.Count) return false;

            var spans = edit.Rims[section.Rim].Line.Spans;
            if (section.Index >= spans.Count) return false;

            // A span that has shrunk below two points has no model any more, so the correspondence is
            // broken however the totals come out.
            if (spans[section.Index].Points.Count < 2) return false;
        }

        foreach (var handle in _handles)
        {
            if (handle.Rim >= edit.Rims.Count) return false;
            if (handle.Index >= edit.Rims[handle.Rim].Line.Anchors.Count) return false;
        }

        foreach (var rim in edit.Rims)
        {
            // Spans too short to draw were never given a model, so they are not counted here either.
            sections += rim.Line.Spans.Count(s => s.Points.Count >= 2);
            handles += rim.Line.Anchors.Count;
        }

        return sections == _sections.Count && handles == _handles.Count;
    }

    private static LineGeometry3D GeometryOf(PartingSpan span)
    {
        var builder = new LineBuilder();
        for (int i = 0; i < span.Points.Count - 1; i++)
            builder.AddLine(ToSharp(span.Points[i]), ToSharp(span.Points[i + 1]));

        return builder.ToLineGeometry3D();
    }

    private static MediaColor RestingColour(PartingSpan span) =>
        span.Condition == PartingLineCondition.Sound ? SoundSection : FaultySection;

    /// <summary>Puts a unit cube at a point on the wall, the given number of millimetres across.</summary>
    private static Transform3D TransformOf(CoreVector3 at, float size)
    {
        var transform = new Transform3DGroup();
        transform.Children.Add(new ScaleTransform3D(size, size, size));
        transform.Children.Add(new TranslateTransform3D(at.X, at.Y, at.Z));

        // Frozen because it is handed to the render thread and never edited afterwards - a state change
        // replaces it rather than mutating it.
        transform.Freeze();
        return transform;
    }

    private static Material SkinOf(MediaColor colour) =>
        new DiffuseMaterial { DiffuseColor = ToColor4(colour) };

    private static Vector3 ToSharp(CoreVector3 v) => new(v.X, v.Y, v.Z);

    private static Color4 ToColor4(MediaColor colour) =>
        new(colour.R / 255f, colour.G / 255f, colour.B / 255f, 1f);
}
