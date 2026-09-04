# Export

Fabolus's role ends at export: it writes the prepared mesh or generated mould to a file. Slicing, 3D printing, and silicone casting are done in other tools and on lab equipment, and are outside Fabolus.

---

## Exporting from the export tab

1. Click the **export** tab.
2. Choose a file format:
   - **STL** — triangle mesh geometry only.
   - **3MF** — a package that, in Fabolus's extended form, also stores the command history and the base mesh, so the project can be re-opened and re-edited.
3. Choose a destination and export.

<!-- IMAGE_PLACEHOLDER: [Figure 9.1: The export tab with the STL / 3MF format options.] -->

### What a 3MF export contains

When 3MF is selected, the export tab lists the **baked operations** captured in the file and identifies the **printable item** (the mesh a slicer will use). Any additional components — such as a base mesh kept for re-editing — are included as non-build references, so a slicer ignores them while Fabolus can still restore them. See [3MF Interchange Specification](../architecture/06-3mf-interchange-specification.md).

---

## After export

The exported file is taken into a slicer to prepare it for a specific printer, printed, and then used to cast the silicone bolus. Those steps, including material choice and casting procedure, are determined by your own workflow and equipment and are not configured in Fabolus.
