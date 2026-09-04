# Overview

Fabolus is a specialized CAD/CAM application for preparing **radiotherapy boluses** and the **sacrificial casting moulds** used to make them. It takes a bolus mesh exported from a Treatment Planning System (TPS) and turns it into a print-ready mould, keeping the full editing history so a project can be re-opened and adjusted later.

A radiotherapy bolus is a tissue-equivalent piece placed against the patient's skin during treatment. Designing and manufacturing one that conforms to curved anatomy is the problem Fabolus addresses on the software side — the clinical prescription (thickness, placement, material) and the physical casting are decided and carried out elsewhere.

<!-- IMAGE_PLACEHOLDER: [Figure 1.1: Fabolus main window with a bolus mesh loaded in the 3D viewport, the step navigation header, and the info panel.] -->

---

## What Fabolus does

Fabolus covers the mesh-preparation and mould-design steps between the TPS export and the 3D printer:

1. **Import & inspect** a bolus mesh and check its geometry (volume, dimensions, watertightness, manifoldness, self-intersections).
2. **Repair** common mesh faults (holes, non-manifold edges, degenerate/self-intersecting triangles).
3. **Smooth** the mesh with a volume-preserving offset that rounds off CT slice stepping.
4. **Orient** the mesh and review overhang angles for printing.
5. **Generate a sacrificial mould** around the bolus, with air channels for casting.
6. **Cut** a mesh along a plane into two halves.
7. **Export** the result as STL, or as an extended 3MF that also stores the command history and base mesh.

Each of these has its own page in this guide.

---

## What Fabolus does not do

Fabolus is not a slicer, a printer driver, or a casting tool, and it does not perform dose calculations. Slicing, 3D printing, silicone casting, and dosimetric verification happen in other software and on lab equipment; this guide describes only the geometry Fabolus produces and the file it hands off.

---

## Why volume-preserving smoothing

A bolus exported from a TPS is built from stacked 2D CT slices, so its surface has stair-stepping ridges. General-purpose vertex-averaging smoothing rounds those off but also shrinks the mesh, which changes its thickness. Fabolus instead expands the surface outward and then contracts it back by the same distance, so broad areas return to their original position while the slice stepping is smoothed. See [Volume-Preserving Smoothing](04-volume-preserving-smoothing.md).
