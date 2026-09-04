# Mould Splitting & Multi-Part Moulds

## When Do You Need a Two-Part Split Mould?

If you 3D print your mould using water-soluble PVA filament, you don't have to worry about shape complexity: the mould simply dissolves away in warm water like sugar.

However, if you print your mould in standard **rigid plastic** (like PLA or PETG), you have to consider **undercuts**:

<!-- IMAGE_PLACEHOLDER: [Figure 8.1: Demoulding Undercut Problem. Diagram illustrating negative draft angles (undercuts) mechanically locking cured silicone inside a monolithic rigid mould vs. clean, non-destructive separation achieved with a 2-part split mould. Dimensions: 800x400px.] -->

### The "Trapped Lightbulb" Problem
Imagine trying to pull a lightbulb out of a rigid bottle—it gets trapped because the opening is narrower than the bulb. In facial anatomy, areas like behind the ear, under the chin, or inside nasal curves create natural undercuts. 

If you cast silicone into a one-piece rigid mould with undercuts, the cured silicone will be locked inside. Pulling it out forcefully will tear delicate silicone features or break the plastic.

**The Solution**: Split the mould into matching **top and bottom halves** (like a plastic Easter egg) so they pull apart effortlessly.

---

## Planar Cutting in Fabolus

Fabolus includes an automated cutting tool that divides any mould into matching halves along a flat cutting plane:

```
                  Top Mould Half
               ┌──────────────────┐
               │   ╭──────────╮   │
═══════════════╪═══╪══════════╪═══╪═══════════════  Cutting Plane
               │   ╰──────────╯   │
               └──────────────────┘
                 Bottom Mould Half
```

<!-- IMAGE_PLACEHOLDER: [Figure 8.2: Interactive Planar Cutting in Fabolus. Viewport screenshot showing the adjustable cutting plane slicing through a mould with normal vector controls and real-time top/bottom preview. Dimensions: 900x550px.] -->

### How It Works:
1. Turn on the **Cutting Plane** tool in the 3D viewport.
2. Drag and tilt the cutting plane to position it through your mould.
3. Fabolus automatically slices the mould into two matching pieces, labeled `Mould (Top)` and `Mould (Bottom)`.
4. Both halves can be exported and 3D printed side-by-side on your printer bed.

---

## Tips for Placing the Cut Line

<!-- IMAGE_PLACEHOLDER: [Figure 8.3: Exploded View of a Two-Part Mould Assembly. 3D exploded render showing Top and Bottom halves, alignment seams, clamping perimeters, and internal silicone bolus core. Dimensions: 900x500px.] -->

1. **Cut Along the Widest Horizon (Parting Line)**:
   Place the cut along the widest perimeter of the anatomy. This ensures neither half has inward-trapping lips that prevent the cured silicone from popping out.
2. **Keep Seams Away From Critical Areas**:
   A tiny amount of liquid silicone can seep into the seam between the two clamped halves, creating a thin "flash" line. Angle your cut so this seam is on the outside or along the edge, rather than running across the central treatment area.
3. **Keep the Halves Flat on the Print Bed**:
   If possible, use a flat horizontal cut so both halves can sit flat on the 3D printer bed without needing external support towers.

---

## Clamping, Casting & Demoulding Checklist

1. **Clean the Seams**: Lightly rub the flat mating faces of the printed halves on fine sandpaper (400 grit) to remove any stray plastic strings.
2. **Clamp Tightly**: Secure the two halves together using standard steel binder clips or rubber bands spaced every inch around the edges.
3. **Inject from Bottom**: Slowly inject your mixed, bubble-free silicone through the bottom port until it fills the cavity and bubbles out of the top vents.
4. **Demoulding**: Once cured (typically 30–60 minutes depending on your silicone):
   - Remove the binder clips.
   - Gently twist a flat plastic pry tool in the seam to pop the halves apart.
   - Lift out the finished silicone bolus and use small scissors to snip off the injection stems and any thin seam lines.

