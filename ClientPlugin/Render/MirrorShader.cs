// As the mirror camera is an off-axis projection it needs to flip its
// X scale to undo the resulting vertex-winding inversion. This shader
// does the X-flip on the way out into the LCD offscreen. It also
// handles rotated screens (block placement) and the per-member
// sub-rect blits that cut a coplanar mirror-group render into its
// constituent LCDs.

using System;
using System.Runtime.InteropServices;
using SharpDX.D3DCompiler;
using SharpDX.Direct3D11;
using SharpDX.Mathematics.Interop;
using VRage.Render11.Common;
using VRage.Render11.RenderContext;
using VRage.Render11.Resources;
using VRage.Utils;
using VRageMath;
using VRageRender;
using D3DBuffer = SharpDX.Direct3D11.Buffer;
using D3DPixelShader = SharpDX.Direct3D11.PixelShader;

namespace ClientPlugin;

/// <summary>
/// Owns the compiled pixel shader + constant buffer that perform the
/// affine UV blit for panel renders, and exposes <see cref="Draw"/> /
/// <see cref="ClearAndCopy"/> as the high-level entry points. One
/// instance for the plugin lifetime, disposed on plugin unload.
/// </summary>
internal sealed class MirrorShader : IDisposable
{
    private D3DPixelShader _shader;
    private D3DBuffer      _buffer;
    private bool           _disposed;

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct CbParams
    {
        public float OriginU, OriginV;   // src UV at dst (0,0)
        public float AxisXU,  AxisXV;    // src UV delta per unit dst.x
        public float AxisYU,  AxisYV;    // src UV delta per unit dst.y
        public float DstW,    DstH;      // destination texture pixel dims
    }

    private const string Hlsl = @"
cbuffer Params : register(b0) {
    float2 srcOrigin; // src UV at destination (0,0)
    float2 srcAxisX;  // src UV delta per unit dst.x
    float2 srcAxisY;  // src UV delta per unit dst.y
    float2 dstSize;   // destination texture pixel dims for svpos -> dst_uv
};

Texture2D<float4> SourceTex : register(t0);
SamplerState      LinearSamp : register(s0);

float4 main(float4 svpos : SV_Position) : SV_TARGET {
    // SV_Position in D3D11 already carries pixel-center coordinates
    // (first pixel = 0.5, 0.5), so dividing it directly yields the
    // correct destination UV. Earlier code added another 0.5 — a D3D9
    // half-pixel-offset workaround that shifts every blit by half a
    // destination texel in +X and +Y on D3D11.
    float2 dst_uv = svpos.xy / dstSize;
    float2 src_uv = srcOrigin + dst_uv.x * srcAxisX + dst_uv.y * srcAxisY;
    return SourceTex.SampleLevel(LinearSamp, src_uv, 0);
}";

    public MirrorShader()
    {
        Setup();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _shader?.Dispose(); } catch { /* defensive */ }
        try { _buffer?.Dispose(); } catch { /* defensive */ }
        _shader = null;
        _buffer = null;
    }

    /// <summary>
    /// Issue the shader: configure pipeline state, update the CB with
    /// <paramref name="xform"/>, draw a fullscreen quad sampling
    /// <paramref name="source"/> into <paramref name="destination"/>.
    /// Does NOT clear the destination, generate mips, or flip the
    /// ready-flag — use <see cref="ClearAndCopy"/> for that.
    /// </summary>
    public void Draw(MyRenderContext rc, ISrvBindable source,
                     IRtvBindable destination, in BlitTransform xform)
    {
        if (_shader == null || _buffer == null) return;

        rc.SetBlendState(MyBlendStateManager.BlendReplaceNoAlphaChannel);
        rc.SetInputLayout(null);
        rc.PixelShader.Set(_shader);
        rc.SetRtv(destination);
        rc.SetDepthStencilState(MyDepthStencilStateManager.IgnoreDepthStencil);
        rc.PixelShader.SetSrv(0, source);

        Vector2I sz = destination.Size;

        // MySpritesManager binds ScissorTestRasterizerState while
        // painting LCD sprites with a scissor rect sized to the LCD's
        // logical area, then resets the rasterizer state to null at
        // end-of-batch but does NOT reset the scissor itself. Bind a
        // non-scissor rasterizer and size the scissor explicitly to the
        // full destination so the blit fills the entire offscreen.
        rc.SetRasterizerState(null);
        rc.SetScissorRectangle(0, 0, sz.X, sz.Y);

        var p = new CbParams
        {
            OriginU = xform.OriginU, OriginV = xform.OriginV,
            AxisXU  = xform.AxisXU,  AxisXV  = xform.AxisXV,
            AxisYU  = xform.AxisYU,  AxisYV  = xform.AxisYV,
            DstW    = sz.X,          DstH    = sz.Y,
        };

        var device = MyRender11.DeviceInstance;
        if (device == null)
        {
            MyLog.Default.WriteLine("[Mirror] MirrorShader.Draw: device unresolved");
            return;
        }
        var ctx = device.ImmediateContext;
        ctx.UpdateSubresource(ref p, _buffer);
        ctx.PixelShader.SetConstantBuffer(0, _buffer);

        MyScreenPass.DrawFullscreenQuad(rc, new MyViewport(sz.X, sz.Y));
    }

    /// <summary>
    /// Final-step blit into an LCD offscreen: clear destination, draw,
    /// regenerate mip chain, and flip the texture's ready-flag so the
    /// LCD mesh shader will start sampling it (it's gated on
    /// <c>IsLoaded</c>, which is false on a fresh
    /// <see cref="IUserGeneratedTexture"/>). The mip regeneration is
    /// load-bearing — the LCD samples lower mips at distance / oblique
    /// angles, and without it the lower mips keep whatever the previous
    /// splash-paint left there.
    /// </summary>
    public void ClearAndCopy(MyRenderContext rc, ISrvBindable source,
                             IRtvBindable destination, in BlitTransform xform)
    {
        rc.ClearRtv(destination, new RawColor4(0f, 0f, 0f, 0f));
        Draw(rc, source, destination, in xform);

        if (destination is ISrvBindable srv) rc.GenerateMips(srv);
        if (destination is IUserGeneratedTexture ugt) ugt.SetTextureReady();
    }

    // ── Setup ────────────────────────────────────────────────────────

    private void Setup()
    {
        var device = MyRender11.DeviceInstance;
        if (device == null)
        {
            MyLog.Default.WriteLine("[Mirror] MirrorShader: device unresolved at construct");
            return;
        }

        try
        {
            using (var comp = ShaderBytecode.Compile(
                Hlsl, "main", "ps_5_0", ShaderFlags.OptimizationLevel3))
            {
                if (comp.HasErrors)
                {
                    MyLog.Default.WriteLine(
                        "[Mirror] MirrorShader compile error: " + (comp.Message ?? "?"));
                    return;
                }
                _shader = new D3DPixelShader(device, comp.Bytecode.Data);
            }

            // 32-byte CB (sizeInBytes must be a multiple of 16; 8 floats = 32).
            var bd = new BufferDescription(
                sizeInBytes:         32,
                usage:               ResourceUsage.Default,
                bindFlags:           BindFlags.ConstantBuffer,
                cpuAccessFlags:      CpuAccessFlags.None,
                optionFlags:         ResourceOptionFlags.None,
                structureByteStride: 0);
            _buffer = new D3DBuffer(device, bd);
        }
        catch (Exception ex)
        {
            MyLog.Default.WriteLine("[Mirror] MirrorShader compile exc: " + ex.Message);
        }
    }
}
