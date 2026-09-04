# Air Channels

Air channels are tubes added to the mould so that, when it is later cast, silicone can enter and air can leave. They are placed in the **mould** tab and subtracted from the mould when you generate it. The casting itself is done outside Fabolus.

---

## Channel types

Fabolus provides three channel types (`AirChannelType`):

- **Straight** — a tube running straight up from the mesh surface.
- **Angled** — leaves the surface perpendicular to the wall, then curves upward (useful on steep walls, where a straight tube would leave a thin edge).
- **Painted** — a channel drawn by dragging along the surface, following a path (useful along a ridge).

<!-- IMAGE_PLACEHOLDER: [Figure 7.1: Straight, Angled, and Painted channels on a bolus mesh.] -->

---

## Channel parameters

The channel controls in the mould tab include:

| Control | Default | What it does |
| :--- | :--- | :--- |
| **Channel Diameter** | `5.0` | Diameter of the main tube. |
| **Tip Diameter** | `3.0` | Diameter at the tip where it meets the mesh. |
| **Tip Length** | `3.0` | Length of the tapered tip. |
| **Tip Depth** | `1.0` | How far the tip penetrates into the mesh. |

A default channel diameter and an **autodetect channels** option are also stored in `PrintBedPreferences` (see [Configuration & Preferences](../reference/configuration-and-preferences.md)).

---

## Placing channels

1. In the **mould** tab, choose a channel type.
2. Move the cursor over the mesh; a preview follows the cursor.
3. Click to place a Straight or Angled channel, or drag to draw a Painted channel.
4. Select a placed channel to adjust its parameters or remove it.

Placed channels are stored with the mould definition, so they are subtracted from the shell when you click **Generate Mould** and are preserved in the saved project.

<!-- IMAGE_PLACEHOLDER: [Figure 7.2: Placing a channel, with the live preview following the cursor.] -->
