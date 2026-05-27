# LCD Mirrors and Cameras

A Pulsar plugin for Space Engineers that renders **real-time mirror
reflections** and **live camera feeds** onto in-game LCD panels. Pairs
with the [MirrorCameraMod](https://steamcommunity.com/sharedfiles/filedetails/?id=3733546359) 
SE mod that handles the LCD apps and terminal UI.

The mod alone shows a "Plugin not loaded" splash. The plugin alone does
nothing. Together they render.

---

## Features

### Mirror panels

- Real **off-axis reflection** — the world is rendered from the eye's
  reflection across the panel plane, with an off-axis frustum bounded
  by the LCD's actual rectangle. Not a billboard, not a camera trick.
- **Coplanar same-grid grouping**: a flat wall of mirrors that share a
  plane is rendered as ONE off-axis pass, then blitted with the correct
  sub-rect to each member's offscreen. A 3×3 mirror wall costs one
  render, not nine.

### Camera panels

- Live view from any camera block on the same grid or any mechanically
  connected subgrid (rotors, pistons, hinges).
- Per-surface camera selection via the LCD's terminal controls.
- Per-surface zoom (1.0×–20.0×).
- Frustum honors the camera's actual lens position (the `"camera"`
  model dummy that `MyCameraBlock.GetViewMatrix` uses).
- Routes through SE's off-axis projection path (msg.FOV=0) so the
  rendered FOV stays constant regardless of the resolution-bucketing
  viewport.

### Scheduling

The orchestrator picks at most `MaxPerFrame` panels to render per batch.

### Cull pipeline

Each batch a unit (one panel group + scoring) runs through this
sequence before becoming eligible for picking. Cheap checks first;
later ones rely on earlier stages having pruned the obvious rejects.

1. **`MinCoverage` cull** — drops units whose screen-projected
   coverage is below `1e-4` (≈ 14×14 pixels on 1080p). Catches
   permanently-broken tiny far panels that would otherwise enter the
   staleness-runaway loop.
2. **`MaxScreenRenderDistance` cull** — closest member's distance
   greater than the block definition's `MaxScreenRenderDistance` (the
   value SE uses to stop drawing the LCD's screen content). Uses
   `CullContext.GroupClosestDistSq` computed by `UnitScorer` so wide
   walls aren't dropped just because the lead member is at the far end.
3. **Range cull** — closest member's distance greater than the
   plugin-wide `Max view distance` (default 40m, slider 5-400m).
   Pushes `"Out of range"` to the panel's status so the splash
   subtitle changes when this fires.
4. **Facing cull** — viewer on the back side of the panel plane.
   Opaque-mirror only; no double-sided flip.
5. **Look-direction cull** — every union-AABB corner outside the look
   cone (cosine ≈ 0.26 ≡ 75° off-axis). Per-corner so wide groups with
   one corner in view don't get dropped.
6. **Frustum cull** — group's union AABB outside the player's view.
7. **Panel-vs-panel occlusion cull** — drops any unit whose
   screen-projected NDC AABB is fully contained inside a closer unit's
   AABB (both clipped to viewport). O(N²) over surviving units, sub-µs
   at typical N. Catches the "wall of close LCDs hides the ones
   behind them" case so we don't burn GPU rendering invisible panels.

### Per-panel render-resolution bucketing

Scene render for each panel runs at a discrete-tier viewport
(`{128, 256, 512, 1024, mainViewCap}` per axis) bucketed by Coverage,
LookFactor, and the main view's resolution. Smaller buckets save GPU
at the cost of a blit upscale (visible as some blur on distant
panels); close + focused panels render at main-view-native resolution.

The bucket math:
- `look = max(0.10, LookFactor)`
- `scale = min(1, sqrt(Coverage × 5 × look))`
- `desired = mainViewCap × scale` per axis
- Anything > 1024 → snap to `mainViewCap` (top tier)
- Else snap UP to the smallest pow2 bucket ≥ desired (`{128, 256, 512, 1024}`)

`Oversample = 5` calibrates so `scale = 1` (mainView tier) at ~5% screen
coverage; below that the bucket steps down smoothly.

## Performance

A single panel render is roughly the cost of a low-LOD scene pass —
SE's full pipeline (lighting, post-process minus a few extras) runs
into a render target sized to fit the panel. The plugin's job is to
make sure that pass happens at most once per panel per frame and that
unnecessary panels never enter the pipeline at all.


The orchestrator rebuilds the group structure only when the
registry version changes (panel added / removed / config changed).
Steady state is one integer compare per batch.

---

## Configuration

In-game plugin config (Pulsar plugin list → "Mirror Camera Panels" →
Configure):

**Master:**

| Setting | Default | Purpose |
|---|---|---|
| Enabled | on | Master switch |

**Performance:**

| Setting | Default | Purpose |
|---|---|---|
| Max per frame | 1 | Hard cap on panels rendered per batch (1-3) |
| Max view distance (m) | 40 | Panels farther than this don't render (5-400) |
| Far clip (m) | 20000 | Far-plane distance for panel renders (1300-100000) |
| Distance resolution LOD | on | Discrete-tier render-resolution scaling |
| Render on pause screen | off | Keep panels rendering when the game is paused |

**Troubleshooting:**

| Setting | Default | Purpose |
|---|---|---|
| Disable shadows | off | Skip directional-shadows pass in panel renders (fixes distant-shadow flicker) |

**Debug:**

| Setting | Default | Purpose |
|---|---|---|
| Debug HUD | off | Top-N scored panels with per-unit signals + world-space outlines |

Per-LCD settings (in the LCD's terminal controls when its app is set
to "Mirror" or "Camera"):

- **Camera source** (Camera app) — listbox of every camera on the
  same grid or any mechanically-connected subgrid.
- **Override camera zoom** (Camera app, when source selected) —
  per-surface zoom override; off uses the camera block's own zoom.
- **Camera View Zoom** (Camera app, when override on) — 1.0×–20.0×.
- **Yaw / Pitch / Roll** (Mirror app, thin LCD variants only) —
  mesh tilt for the panel's visible screen, ±45°.

---

## Architecture

```
┌──────────────────────────────┐         ┌──────────────────────────────┐
│ MirrorCameraMod              │         │ MirrorPlugin                 │
│ (SE mod)                     │         │ (Pulsar plugin, ClientPlugin)│
├──────────────────────────────┤         ├──────────────────────────────┤
│ MirrorScript / CameraScript  │         │ ReflectionModBridge          │
│   ▼                          │         │   reads PanelRegistry via    │
│ PanelRegistry                │ ──────► │   reflection per Sync        │
│   sim-thread snapshot of all │         │   ▼                          │
│   active panels + status     │         │ SurfaceRegistry              │
│                              │ ◄────── │   plugin-side mirror,        │
│ MirrorSession                │ status  │   versioned for cache gating │
│   terminal UI, storage I/O,  │ writes  │   ▼                          │
│   MP sync via                │         │ PanelGroupBuilder            │
│   SettingsNetwork            │         │   merges coplanar same-grid  │
│                              │         │   mirrors into groups        │
│                              │         │   ▼                          │
│                              │         │ PanelBatchOrchestrator       │
│                              │         │   refresh planes → score →   │
│                              │         │   cull → pick → render       │
└──────────────────────────────┘         └──────────────────────────────┘
```

Cross-assembly access uses reflection (the SE mod assembly is renamed
by the engine at load time, no static type reference is resolvable).
Field readers are compiled to delegates at resolve time so per-sync
reads don't pay the boxing cost.

---

## Building

```sh
dotnet build Mirror.sln
```

The solution lives at `MirrorPlugin/Mirror.sln`. `Directory.Build.props`
points at the SE `Bin64` directory; the post-build step copies the
output DLL into `%APPDATA%\Pulsar\Legacy\Local\Mirror.dll`. Targets
`net48`. Uses Harmony to patch `MyRender11.DrawGameScene` and a few
engine helpers, plus the Krafs publicizer trick to access internals.

---

## Debugging

Turn on **Debug HUD** in the plugin config:

- Top-left list of the top-N scored render units. Each row shows the
  picked-marker, focus + focus-with-staleness scores, bucketed
  viewport size, far plane, group size, mode, plane normal, distance
  factors, coverage, distance.
- For whatever's under the crosshair (ray-vs-AABB intersect): a
  detailed block — union AABB, member breakdown, tentative RT size
  vs main view cap, the chosen viewport bucket vs LCD RT.
- World-space wireframe rectangles around every in-view group
  (picked = green, others = white).

The engine log (`%APPDATA%/SpaceEngineers/SpaceEngineers_*.log`) gets
throttled diag lines under `[Mirror/Diag]`. Most useful entries:
- `renderfail: block=... reason='...'` for every failed render
  (identifies block + cause). Unconditional; not throttled.
- Group / cull / render summary lines per batch.

---

## Distribution

Plugins are released exclusively on PluginHub. The DLL is compiled on
the player's machine from this repository's GitHub source, identified
by the PluginHub registration. Plugins are reviewed for safety and
security on submission, on a best-effort basis.

To report bugs or contribute, file an issue / PR on the repo.
