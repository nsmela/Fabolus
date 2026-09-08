# Overview

Fabolus is used to prepare a 3D mesh for 3D printing either as a smoothed, solid plastic print or as a sacrificial mould for silicone casting. It takes a mesh exported from a Treatment Planning System (TPS) and turns it into a print-ready file, keeping the full editing history so a project can be re-opened and adjusted later.

<!-- IMAGE_PLACEHOLDER: [Figure 1.1: Fabolus main window with a bolus mesh loaded in the 3D viewport, the step navigation header, and the info panel.] -->

---

## What Fabolus does

Fabolus covers the mesh-preparation and mould-design steps between the TPS export and the 3D printer:

1. **Import & inspect** a 3D mesh and check its geometry (volume, dimensions, watertightness, manifoldness, self-intersections, and multi-mesh components).
2. **Repair** common mesh faults (holes, non-manifold edges, self-intersecting triangles, and multi-mesh / disconnected shells).
3. **Smooth** the mesh with a volume-preserving offset that rounds off sharp stepped features.
4. **Orient** the mesh and review overhang angles for printing.
5. **Generate a sacrificial mould** around the bolus, with air channels for casting.
6. **Split a mould (Experimental)** into two printable parts along a cutting plane.
7. **Export** as an extended 3MF package (which preserves the base mesh and complete editing history for future adjustments), or as standard STL/OBJ files for direct slicing.

Each of these has its own page in this guide.

---

## What Fabolus does not do

Fabolus is not a slicer, a printer driver, or a casting tool, and it does not perform dose calculations. Slicing, 3D printing, silicone casting, and dosimetric verification happen in other software and on lab equipment; this guide describes only the geometry Fabolus produces and the file it hands off.

---

## Why volume-preserving smoothing

When 2D image contours from a CT scan are converted into a 3D mesh, the surface ends up with sharp features and stair-stepping ridges. 

Standard 3D smoothing rounds off those bumps, but it also shrinks the model—making walls thinner than planned. Fabolus smooths out the sharp features while strictly preserving the original thickness and volume. See [Volume-Preserving Smoothing](04-volume-preserving-smoothing.md).
