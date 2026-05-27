using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using VRage.Utils;
using IMyCubeBlock   = VRage.Game.ModAPI.Ingame.IMyCubeBlock;
using IMyTextSurface = Sandbox.ModAPI.Ingame.IMyTextSurface;

namespace ClientPlugin;

/// <summary>
/// Reflection-based bridge to <c>MirrorCameraMod.PanelRegistry</c>. The
/// mod assembly is renamed by SE at load time (folder + scripts subdir
/// concatenated), so a static type reference would never resolve;
/// instead this scans loaded assemblies for the registry type and
/// caches a set of compiled field-reader delegates that the per-sync
/// enumeration uses.
///
/// Compiled delegates (vs. raw <c>FieldInfo.GetValue</c>) avoid the
/// per-field boxing cost on every sync, even though sync only runs
/// once per sim tick. Resolution overhead is paid once on session
/// start.
/// </summary>
internal sealed class ReflectionModBridge
{
    private const int MinSupportedModVersion = 4;
    private const int MaxSupportedModVersion = 4;

    private Func<IEnumerable>             _enumerate;
    private Func<object, IMyTextSurface>  _readSurface;
    private Func<object, IMyCubeBlock>    _readBlock;
    private Func<object, int>             _readSurfaceIdx;
    private Func<object, int>             _readMode;
    private Func<object, IMyCubeBlock>    _readCameraBlock;
    private Func<object, float>           _readZoom;
    private Func<object, float>           _readMirrorAngleDegX;
    private Func<object, float>           _readMirrorAngleDegY;
    private Func<object, float>           _readMirrorAngleDegZ;
    private Action<long, int, string>     _writeStatus;

    public bool IsResolved => _enumerate != null;

    public bool TryResolve()
    {
        if (IsResolved) return true;

        // SE renames the mod assembly to "<ModFolderName>_<ScriptsSubfolder>"
        // (e.g. "MirrorCamera_MirrorCamera"), so the simple Type.GetType
        // path with an assembly-qualified name never matches. Scan all
        // loaded assemblies for the registry type instead.
        //
        // .NET Framework can't unload assemblies, so each session SE
        // re-compiles the mod the AppDomain accumulates one new
        // MirrorCameraMod assembly per session-load. The PRE-cached
        // Type from session N still resolves but its static fields
        // belong to session N's PanelRegistry — which session N+1's mod
        // code isn't writing to. We want the MOST RECENTLY loaded
        // matching type (= the current session's PanelRegistry), so we
        // iterate all assemblies and prefer the last match rather than
        // breaking on the first.
        Type registryType = null;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var probe = asm.GetType("MirrorCameraMod.PanelRegistry", throwOnError: false);
            if (probe != null) registryType = probe;
        }
        if (registryType == null) return false;

        // ApiVersion gate: lets the plugin reject incompatible mod versions
        // without the runtime errors of binding to fields that no longer exist.
        int apiVersion;
        try
        {
            var f = registryType.GetField("ApiVersion",
                BindingFlags.Public | BindingFlags.Static);
            if (f == null) { LogMissing("PanelRegistry.ApiVersion"); return false; }
            apiVersion = (int)f.GetValue(null);
        }
        catch (Exception ex)
        {
            MyLog.Default.WriteLine(
                "[Mirror] Failed reading PanelRegistry.ApiVersion: " + ex.Message);
            return false;
        }
        if (apiVersion < MinSupportedModVersion || apiVersion > MaxSupportedModVersion)
        {
            MyLog.Default.WriteLine(
                $"[Mirror] MirrorCameraMod ApiVersion {apiVersion} outside "
                + $"supported range ({MinSupportedModVersion}..{MaxSupportedModVersion})");
            return false;
        }

        var enumMi = registryType.GetMethod("EnumeratePanels",
            BindingFlags.Public | BindingFlags.Static);
        if (enumMi == null) { LogMissing("PanelRegistry.EnumeratePanels"); return false; }

        var panelInfoType = registryType.Assembly.GetType(
            "MirrorCameraMod.PanelRegistry+PanelInfo", throwOnError: false);
        if (panelInfoType == null) { LogMissing("PanelRegistry.PanelInfo"); return false; }

        try
        {
            _readSurface         = CompileReader<IMyTextSurface>(panelInfoType, "Surface");
            _readBlock           = CompileReader<IMyCubeBlock>(panelInfoType, "Block");
            _readSurfaceIdx      = CompileReader<int>(panelInfoType, "SurfaceIdx");
            _readMode            = CompileEnumIntReader(panelInfoType, "Mode");
            _readCameraBlock     = CompileReader<IMyCubeBlock>(panelInfoType, "CameraBlock");
            _readZoom            = CompileReader<float>(panelInfoType, "Zoom");
            _readMirrorAngleDegX = CompileReader<float>(panelInfoType, "MirrorAngleDegX");
            _readMirrorAngleDegY = CompileReader<float>(panelInfoType, "MirrorAngleDegY");
            _readMirrorAngleDegZ = CompileReader<float>(panelInfoType, "MirrorAngleDegZ");
            _enumerate           = (Func<IEnumerable>)Delegate.CreateDelegate(
                                       typeof(Func<IEnumerable>), enumMi);
        }
        catch (Exception ex)
        {
            ClearCache();
            MyLog.Default.WriteLine(
                "[Mirror] Failed binding to PanelRegistry.PanelInfo fields: " + ex);
            return false;
        }

        // Optional binding: SetStatus was added after ApiVersion 1
        // shipped, so older mod builds may lack it. Missing method
        // doesn't fail resolution; status writes just become no-ops.
        var setStatusMi = registryType.GetMethod("SetStatus",
            BindingFlags.Public | BindingFlags.Static);
        if (setStatusMi != null)
        {
            try
            {
                _writeStatus = (Action<long, int, string>)Delegate.CreateDelegate(
                    typeof(Action<long, int, string>), setStatusMi);
            }
            catch (Exception ex)
            {
                _writeStatus = null;
                MyLog.Default.WriteLine(
                    "[Mirror] PanelRegistry.SetStatus has unexpected signature: " + ex.Message);
            }
        }

        return true;
    }

    public void ClearCache()
    {
        _enumerate           = null;
        _readSurface         = null;
        _readBlock           = null;
        _readSurfaceIdx      = null;
        _readMode            = null;
        _readCameraBlock     = null;
        _readZoom            = null;
        _readMirrorAngleDegX = null;
        _readMirrorAngleDegY = null;
        _readMirrorAngleDegZ = null;
        _writeStatus         = null;
    }

    public void WriteStatus(long blockId, int surfaceIdx, string status)
    {
        var w = _writeStatus;
        if (w == null) return;
        try { w(blockId, surfaceIdx, status); }
        catch (Exception ex)
        {
            MyLog.Default.WriteLine(
                "[Mirror] WriteStatus threw: " + ex.Message);
        }
    }

    public IEnumerable<PanelInfoSnapshot> EnumeratePanels()
    {
        if (_enumerate == null) yield break;

        IEnumerable raw;
        try { raw = _enumerate(); }
        catch (Exception ex)
        {
            MyLog.Default.WriteLine(
                "[Mirror] EnumeratePanels threw: " + ex.Message);
            yield break;
        }

        foreach (var entry in raw)
        {
            if (entry == null) continue;
            yield return new PanelInfoSnapshot(
                surface:         _readSurface(entry),
                block:           _readBlock(entry),
                surfaceIdx:      _readSurfaceIdx(entry),
                mode:            (PanelMode)_readMode(entry),
                cameraBlock:     _readCameraBlock(entry),
                zoom:            _readZoom(entry),
                mirrorAngleDegX: _readMirrorAngleDegX(entry),
                mirrorAngleDegY: _readMirrorAngleDegY(entry),
                mirrorAngleDegZ: _readMirrorAngleDegZ(entry));
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────

    /// <summary>Compile a field reader: <c>(object o) =&gt; ((T)o).FieldName</c>.
    /// Avoids <see cref="FieldInfo.GetValue"/>'s per-call boxing for value
    /// types and the slower reflection invoke path for reference types.</summary>
    private static Func<object, T> CompileReader<T>(Type ownerType, string fieldName)
    {
        var field = ownerType.GetField(fieldName,
            BindingFlags.Public | BindingFlags.Instance);
        if (field == null)
            throw new InvalidOperationException(
                $"{ownerType.FullName}.{fieldName} not found");

        var p = Expression.Parameter(typeof(object), "o");
        var body = Expression.Field(Expression.Convert(p, ownerType), field);
        return Expression.Lambda<Func<object, T>>(body, p).Compile();
    }

    /// <summary>Specialized reader for the mod's PanelMode enum: reads
    /// the field via reflection (cannot cast enum to int directly in
    /// an expression tree without knowing the underlying type at JIT
    /// time) and uses <see cref="Convert.ToInt32"/> for safe conversion
    /// regardless of the enum's underlying type.</summary>
    private static Func<object, int> CompileEnumIntReader(Type ownerType, string fieldName)
    {
        var field = ownerType.GetField(fieldName,
            BindingFlags.Public | BindingFlags.Instance);
        if (field == null)
            throw new InvalidOperationException(
                $"{ownerType.FullName}.{fieldName} not found");

        var p = Expression.Parameter(typeof(object), "o");
        // Cast through long → int so a SByte/UInt32/etc. underlying type still works.
        var fieldAccess = Expression.Field(Expression.Convert(p, ownerType), field);
        var asInt = Expression.Convert(
            Expression.Convert(fieldAccess, field.FieldType.GetEnumUnderlyingType()),
            typeof(int));
        return Expression.Lambda<Func<object, int>>(asInt, p).Compile();
    }

    private static void LogMissing(string name) =>
        MyLog.Default.WriteLine("[Mirror] " + name + " missing — bridge bind failed");
}
