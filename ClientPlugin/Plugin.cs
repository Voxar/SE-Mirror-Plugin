using System;
using System.Reflection;
using ClientPlugin.Settings;
using ClientPlugin.Settings.Layouts;
using HarmonyLib;
using Sandbox.Graphics.GUI;
using VRage.Plugins;
using VRage.Utils;

// Set the assembly version manually if compiled by Pulsar (it won't create what was in AssemblyInfo.cs before)
#if !DEV_BUILD
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
#endif

namespace ClientPlugin;

// ReSharper disable once UnusedType.Global
/// <summary>
/// Plugin entry point. Composes every service (constructor injection,
/// no static reach-arounds), installs Harmony patches, owns disposal.
///
/// <para>Harmony patches reach the orchestrator via static
/// <see cref="Current"/>; patches can't take constructor parameters
/// so this is the documented escape hatch.</para>
/// </summary>
public class Plugin : IPlugin
{
    public const string Name = "Mirror";

    /// <summary>Template-style live instance handle. Used by the
    /// settings dialog plumbing.</summary>
    public static Plugin Instance { get; private set; }

    /// <summary>Alias for <see cref="Instance"/> kept for the Harmony
    /// patches (they were written against this name in the original
    /// plugin and the rename would just be churn). Null between
    /// <see cref="Dispose"/> and re-construction.</summary>
    internal static Plugin Current => Instance;

    internal PanelBatchOrchestrator Orchestrator => _orchestrator;
    internal FirstPersonHeadFix     HeadFix      => _headFix;
    internal PanelGroupBuilder      GroupBuilder => _groupBuilder;

    // ── Services (owned, disposed in reverse order) ──────────────

    private ReflectionModBridge       _modBridge;
    private SurfaceRegistry           _surfaceRegistry;
    private FirstPersonHeadFix       _headFix;
    private MirrorShader              _blitShader;
    private PanelGroupBuilder        _groupBuilder;
    private PanelBatchOrchestrator   _orchestrator;
    private Harmony                   _harmony;

    private SettingsGenerator settingsGenerator;

    // The MirrorCameraMod.PanelRegistry type we last saw as the
    // "latest" in the AppDomain. SE recompiles the mod each session
    // load, and .NET Framework can't unload the old assembly — so
    // the AppDomain accumulates one type-per-session-load. When the
    // latest changes, we're bound to a stale one (its static state
    // belongs to a session no one's writing to anymore) and need to
    // re-bind. Tracking by Type reference rather than session
    // reference is direct: the bind goes stale exactly when a new
    // assembly appears, regardless of how SE signals the session
    // change.
    private Type _lastSeenRegistryType;

    // ── IPlugin ──────────────────────────────────────────────────────

    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public void Init(object gameInstance)
    {
        Instance = this;
        settingsGenerator = new SettingsGenerator();

        Compose();

        _harmony = new Harmony(Name);
        _harmony.PatchAll(Assembly.GetExecutingAssembly());
    }

    public void Update()
    {
        // Detect when SE has compiled+loaded a new MirrorCameraMod
        // assembly (= session reload, since the old assembly can't
        // unload). Throttled because AppDomain.GetAssemblies() allocates
        // a fresh array each call and the load-event is rare enough that
        // a 1-second polling delay before re-binding is invisible.
        _modReloadProbeTicks++;
        if (_modReloadProbeTicks >= ModReloadProbeIntervalTicks)
        {
            _modReloadProbeTicks = 0;
            DetectModReloadAndInvalidateBridge();
        }

        // Sim-thread tick. Each call is cheap when nothing changed:
        // SurfaceRegistry.Sync no-ops on identical state; HeadFix
        // only polls every N ticks.
        _surfaceRegistry?.Sync();
        _headFix?.OnSimTick(true);
    }

    // ~60 ticks/s sim — every ~1 s.
    private const int ModReloadProbeIntervalTicks = 60;
    private int _modReloadProbeTicks;

    private void DetectModReloadAndInvalidateBridge()
    {
        // Find the most-recently-loaded MirrorCameraMod.PanelRegistry
        // type. AppDomain assemblies are in load order; the LAST
        // matching is the newest. If no mod is loaded yet, latest=null.
        Type latest = null;
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int i = 0; i < assemblies.Length; i++)
        {
            var probe = assemblies[i].GetType("MirrorCameraMod.PanelRegistry", throwOnError: false);
            if (probe != null) latest = probe;
        }
        if (latest == null)
        {
            // Mod assembly went away (player disabled the mod mid-
            // session). If we were previously bound to one, clear the
            // bridge and the sentinel so a future reload triggers a
            // fresh "new type" detection instead of comparing against
            // the stale Type reference.
            if (_lastSeenRegistryType != null)
            {
                _lastSeenRegistryType = null;
                _modBridge?.ClearCache();
            }
            return;
        }
        if (ReferenceEquals(latest, _lastSeenRegistryType)) return;
        // New assembly appeared (or first time seeing one). Drop the
        // bridge so SurfaceRegistry's per-tick TryResolve re-binds to
        // the new statics.
        _lastSeenRegistryType = latest;
        _modBridge?.ClearCache();
    }

    // ReSharper disable once UnusedMember.Global
    public void OpenConfigDialog()
    {
        settingsGenerator.SetLayout<Simple>();
        MyGuiSandbox.AddScreen(settingsGenerator.Dialog);
    }

    public void Dispose()
    {
        // Save state and close resources here, called when the game exits (not guaranteed!)
        // IMPORTANT: Do NOT call harmony.UnpatchAll() here! It may break other plugins.
        try { _harmony?.UnpatchAll(_harmony.Id); } catch { /* defensive */ }
        _harmony = null;

        // Dispose GPU resources before the device goes away.
        try { _blitShader?.Dispose(); } catch { /* defensive */ }
        _blitShader = null;

        Instance = null;
    }

    // ── Composition ──────────────────────────────────────────────

    /// <summary>
    /// Wire every service. Constructor injection only — no service
    /// locator, no static reach-arounds. Adding a new service is a
    /// matter of constructing it here with its dependencies.
    /// </summary>
    private void Compose()
    {
        IMirrorPluginSettings settings = Config.Current;

        // Cross-assembly mod bridge + surface registry + plugin→mod
        // status channel.
        _modBridge       = new ReflectionModBridge();
        var statusSink   = new ModBridgeStatusSink(_modBridge);
        _surfaceRegistry = new SurfaceRegistry(_modBridge, statusSink);

        // Engine wrappers — stateless or single-instance, no
        // ordering constraints.
        var planeResolver     = new ScreenPlaneResolver();
        var offscreenResolver = new LcdOffscreenResolver();
        var actorMatrix       = new ActorMatrixSource();
        var cameraResolver    = new CameraBlockResolver(actorMatrix);
        _headFix              = new FirstPersonHeadFix();

        // GPU resources.
        _blitShader = new MirrorShader();

        // Render pipeline (owns the FPV head-fix hook; no other deps).
        var pipeline = new RenderScene(_headFix);

        // Per-mode renderer strategies + dispatcher.
        var mirrorRenderer = new MirrorPanelRenderer(
            pipeline, _blitShader, offscreenResolver, planeResolver, actorMatrix, settings);
        var cameraRenderer = new CameraPanelRenderer(
            pipeline, _blitShader, offscreenResolver, cameraResolver, settings);
        var dispatcher = new PanelRendererDispatcher(mirrorRenderer, cameraRenderer);

        // Multi-member group renderer.
        var groupRenderer = new MirrorGroupRenderer(
            pipeline, _blitShader, offscreenResolver, settings);

        // Diag log — needed by the grouping builder for merge-
        // reject counters and by the orchestrator for batch
        // summaries. Constructed up here so dependents can take it
        // via constructor.
        var diag = new ThrottledDiagLog();

        // Grouping (incremental on registry version).
        _groupBuilder      = new PanelGroupBuilder(planeResolver, actorMatrix, settings);
        var planeRefresher = new PanelGroupPlaneRefresher(planeResolver, actorMatrix, settings);

        // Scheduling.
        var scorer    = new UnitScorer();
        var cullChain = PanelCullChain.Default(settings, statusSink);
        var slot0Score = new FocusScore();      // also used by PanelDebug HUD ranking
        // Single selector for every slot: focus-threshold pass
        // picks the panel the player is clearly looking at;
        // fallback pass picks by focus + staleness so panels
        // without a clear focus all rotate. Same instance wired
        // to both slot 0 and slot 1+ — the orchestrator's slot
        // distinction collapses to "call PickNext, mark, repeat".
        var picker = new FocusAndStalenessSelector(slot0Score);
        var slot0  = picker;
        var slot1  = picker;

        var bucketPolicy = new LcdRtBucketPolicy();

        // Wire the debug HUD overlay — sees the same score function
        // the slot-0 selector uses AND the same bucket policy the
        // orchestrator applies, so the displayed vp= dims match
        // what actually renders.
        PanelDebug.ConfigureHud(slot0Score, picker, bucketPolicy, settings, offscreenResolver);

        _orchestrator = new PanelBatchOrchestrator(
            registry:          _surfaceRegistry,
            groupBuilder:      _groupBuilder,
            planeRefresher:    planeRefresher,
            scorer:            scorer,
            cullChain:         cullChain,
            slot0Selector:     slot0,
            slot1PlusSelector: slot1,
            panelDispatcher:   dispatcher,
            groupRenderer:     groupRenderer,
            offscreenResolver: offscreenResolver,
            bucketPolicy:      bucketPolicy,
            statusSink:        statusSink,
            settings:          settings,
            diag:              diag);
    }

    //TODO: Uncomment and use this method to load asset files
    /*public void LoadAssets(string folder)
    {

    }*/
}
