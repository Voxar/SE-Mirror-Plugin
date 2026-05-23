# MirrorCameraPlugin

A Pulsar plugin for Space Engineers that renders **real-time mirror
reflections** and **live camera feeds** onto in-game LCD panels. Pairs
with the `MirrorCameraMod` SE mod (the mod owns the LCD apps and
terminal UI; the plugin owns the actual rendering).

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
- Adjacency-gated merging — touching panels merge, distant coplanar
  panels stay separate.
- "Always group touching" override (default on) — edge-to-edge mirror
  walls of arbitrary size merge into one render regardless of the
  proportional render-target budget.

### Camera panels

- Live view from any camera block on the same grid or any mechanically
  connected subgrid (rotors, pistons, hinges).
- Per-surface camera selection via the LCD's terminal controls.
- Per-surface zoom (1.0×–15.0×) and render-range sliders.
- Frustum honors the camera's actual lens position (the `"camera"`
  model dummy that `MyCameraBlock.GetViewMatrix` uses).

### Scheduling

The orchestrator picks at most `MaxPerFrame` panels to render per batch.
Same `FocusAndStalenessSelector` instance runs both slots; behavior:

- **Pass 1 — focus threshold**: any unit whose
  `FocusScore = Coverage × LookFactor²⁰ / max(1, DistSq)` exceeds
  `0.001` is "focused" — the player is clearly aimed at it. Highest-
  scoring focused unit wins.
- **Pass 2 — focus + staleness**: when no unit passes the threshold,
  pick by `focus + staleness × 1e-5`. Stale panels climb past
  peripheral-but-not-stale ones over many frames.
- **`MaxPerFrame=1` + focused-camera edge case** — control-room mode.
  Cameras don't ghost between frames so a single-slot budget shouldn't
  lock to one aimed-at camera. Mirrors keep focus+staleness priority;
  cameras pure round-robin via a cursor.
- **Player-stationary + focused-mirror edge case** — mirror ghosting
  only shows under TRANSLATION (reflected-eye moves), not rotation.
  When the eye isn't moving every visible mirror cycles via a cursor,
  giving each equal update share.
- **Render-failure backoff** — on render failure the staleness clock
  advances anyway, so a permanently-broken panel can't accumulate
  unbounded priority and dominate every batch.

### Cull pipeline

Each batch a unit (one panel group + scoring) runs through this
sequence before becoming eligible for picking. Cheap checks first;
later ones rely on earlier stages having pruned the obvious rejects.

1. **`MinCoverage` cull** — drops units whose screen-projected
   coverage is below `1e-4` (≈ 14×14 pixels on 1080p). Catches
   permanently-broken tiny far panels that would otherwise enter the
   staleness-runaway loop.
2. **Range cull** — group's screen-center farther from the eye than
   the panel's configured `Render Range` (default 40m, max 500m).
3. **Facing cull** — viewer is on the back side of the panel plane.
   Double-sided LCDs flip the plane normal instead so the reflection
   still works from either side.
4. **Look-direction cull** — every AABB corner outside the look cone
   (cosine ≈ 0.26 ≡ 75° off-axis). Per-corner so wide groups with one
   corner in view don't get dropped.
5. **Frustum cull** — group's union AABB outside the player's view.
6. **Panel-vs-panel occlusion cull** — drops any unit whose
   screen-projected NDC AABB is fully contained inside a closer unit's
   AABB (both clipped to viewport). O(N²) over surviving units, sub-µs
   at typical N. Catches the "wall of close LCDs hides the ones
   behind them" case so we don't burn GPU rendering invisible panels.

The diag log line `cull: built=N stdCull=M survived=K` distinguishes
the first five stages from the panel-vs-panel pass — N−M panels are
dropped by frustum/facing/look/range/coverage; M−K are dropped by
panel-vs-panel occlusion.

### Per-panel render-resolution bucketing

Scene render for each panel runs at a power-of-2 viewport (`{128, 256,
512, 1024}` per axis) bucketed by Coverage and capped at the LCD's
offscreen RT size. Smaller buckets save GPU at the cost of a blit
upscale (visible as some blur on distant panels).

The bucket math:
- `scale = sqrt(Coverage × 2)` clamped to `[0..1]`
- `desired = lcdSize × scale` per axis
- Snap each axis UP to the smallest pow2 bucket ≥ desired
- Cap each axis at LCD RT size AND at main view resolution

For a 512×512 LCD on 1920×1080:

| Coverage | scale | desired px | bucket |
|---|---|---|---|
| 0.5 | 1.0 | 512 | 512 (1:1) |
| 0.25 | 0.71 | 362 | 512 (1:1) |
| 0.1 | 0.45 | 229 | 256 (2× upscale) |
| 0.025 | 0.22 | 115 | 128 (4× upscale) |

### Other

- **Per-panel status channel** — plugin reports per-panel state
  (`"rendered"`, `"failed: ..."`) back to the mod, which surfaces it
  as the splash subtitle.
- **Cockpit head fix** — character head/eye materials masked in FPV
  are unmasked during panel renders so the mirror reflects the full
  character even when seated.
- **Optional shadow suppression** — turn off the engine's directional
  shadows pass for panel renders to avoid cascade flickering.

---

## Performance

A single panel render is roughly the cost of a low-LOD scene pass —
SE's full pipeline (lighting, post-process minus a few extras) runs
into a render target sized to fit the panel. The plugin's job is to
make sure that pass happens at most once per panel per frame and that
unnecessary panels never enter the pipeline at all.

Knobs from most-impactful to least:

- **`MaxPerFrame`** (default 1) — hard cap on panels rendered per
  batch. Single most impactful perf setting.
- **Distance resolution LOD** (default off) — power-of-2 bucketing
  described above. Cuts GPU work on distant panels at the cost of
  some blit-upscale blur.
- **Render shadows** (default on) — directional shadow pass is most
  of the per-panel GPU cost. Off saves a lot but can leave a slight
  flat look on the reflection.
- **Far clip (m)** (default 5000) — far-plane distance. Lower = less
  shadow-cascade / distant-LOD work.

The orchestrator also rebuilds the group structure only when the
registry version changes (panel added / removed / config changed).
Steady state is one integer compare per batch.

---

## Configuration

In-game plugin config (Pulsar plugin list → "Mirror Camera Panels" →
Configure):

| Setting | Default | Purpose |
|---|---|---|
| Enabled | on | Master switch |
| Max per frame | 1 | Hard cap on panels rendered per batch |
| Head fix | on | Show character head/face during panel renders |
| Far clip (m) | 5000 | Far-plane distance for panel renders |
| Render shadows | on | Include directional shadows in panel renders |
| Always group touching | on | Merge edge-to-edge mirror walls regardless of RT budget |
| Debug HUD | off | Top-N scored panels with per-unit signals + world-space outlines |
| Distance resolution LOD | off | Pow2-bucketed render-resolution scaling |

Per-LCD settings (in the LCD's terminal controls when its app is set
to "Mirror" or "Camera"):

- **Camera source** (Camera app) — listbox of every camera on the
  same grid or any mechanically-connected subgrid.
- **Camera zoom** (Camera app, when source selected) — 1.0×–15.0×.
- **Render range** — meters. Beyond this the LCD shows its splash.
  Default 40m, max 500m.

---

## Architecture

```
┌──────────────────────────────┐         ┌──────────────────────────────┐
│ MirrorCameraMod              │         │ MirrorCameraPlugin           │
│ (SE mod)                     │         │ (Pulsar plugin)              │
├──────────────────────────────┤         ├──────────────────────────────┤
│ MirrorScript / CameraScript  │         │ ReflectionModBridge          │
│   ▼                          │         │   reads PanelRegistry via    │
│ PanelRegistry                │ ──────► │   reflection per Sync        │
│   sim-thread snapshot of all │         │   ▼                          │
│   active panels + status     │         │ SurfaceRegistry              │
│                              │ ◄────── │   plugin-side mirror,        │
│ MirrorSession                │ status  │   versioned for cache gating │
│   terminal UI, storage I/O   │ writes  │   ▼                          │
│                              │         │ PanelGroupBuilder            │
│                              │         │   merges coplanar same-grid  │
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
dotnet build MirrorCameraPlugin/MirrorCameraPlugin.csproj \
  /p:Bin64="path/to/SpaceEngineers/Bin64" \
  /p:PulsarDir="path/to/Pulsar"
```

Targets `net48`. Uses Harmony and the Krafs publicizer trick to access
engine internals.

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
throttled diag lines under `[MirrorCameraPlugin/Diag]`:
- `groups: total=N solo=M grouped=K (members=L)` per rebuild
- `merge: reject mode=... grid=... ...` — rejection counts per cause
- `cull: built=N stdCull=M survived=K` per batch
- `render: picked=N rendered=M failed=L` per batch
- `renderfail: block=... reason='...'` for every failed render
  (identifies block + cause)

---

## Distribution

Plugins are released exclusively on PluginHub. The DLL is compiled on
the player's machine from this repository's GitHub source, identified
by the PluginHub registration. Plugins are reviewed for safety and
security on submission, on a best-effort basis.

To report bugs or contribute, file an issue / PR on the repo.
