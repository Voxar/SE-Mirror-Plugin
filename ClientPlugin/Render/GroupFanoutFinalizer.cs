using VRage.Render11.RenderContext;
using VRage.Render11.Resources;

namespace ClientPlugin;

/// <summary>
/// Multi-member mirror group finalizer. For each resolved member's
/// offscreen, computes the rotation-appropriate affine UV transform
/// into the shared post-process result and clears+blits its sub-rect.
///
/// <para>Holds references to the group's resolved member offscreens
/// (pre-allocated array, parallel to <see cref="PanelGroup.Members"/>)
/// and the union AABB dimensions for the UV computation. Constructed
/// per render; lives only on the stack (struct).</para>
/// </summary>
internal readonly struct GroupFanoutFinalizer : IRenderViewFinalizer
{
    private readonly MirrorShader   _shader;
    private readonly PanelGroup    _group;
    private readonly IRtvBindable[] _resolvedOffscreens;   // parallel to group.Members; null entries skipped
    private readonly float          _invUnionW;
    private readonly float          _invUnionH;

    public GroupFanoutFinalizer(
        MirrorShader shader, PanelGroup group,
        IRtvBindable[] resolvedOffscreens,
        double unionW, double unionH)
    {
        _shader             = shader;
        _group              = group;
        _resolvedOffscreens = resolvedOffscreens;
        _invUnionW          = (float)(1.0 / unionW);
        _invUnionH          = (float)(1.0 / unionH);
    }

    public void Run(MyRenderContext rc, IBorrowedCustomTexture postProcessed)
    {
        // Shared RT layout (post-process sRGB):
        //   pixel column 0     = world's gUMax (projection X-flip)
        //   pixel column W-1   = world's gUMin
        //   pixel row    0     = world's gVMax (camUp top)
        //   pixel row    H-1   = world's gVMin
        // Each LCD member's offscreen wants:
        //   dst (0,0) = member's top-left in its own LCD frame.
        // The four rotation cases map dst.xy into source UV via an
        // affine xform (origin + axisX*dst.x + axisY*dst.y).

        var members = _group.Members;
        float gUMax = (float)_group.UMax;
        float gVMax = (float)_group.VMax;

        for (int i = 0; i < members.Count; i++)
        {
            var off = _resolvedOffscreens[i];
            if (off == null) continue;   // member not yet ready — skip blit, group renders the rest

            var mem = members[i];
            float mUMin = (float)mem.UMin, mUMax = (float)mem.UMax;
            float mVMin = (float)mem.VMin, mVMax = (float)mem.VMax;

            BlitTransform xform;
            switch (mem.Rotation)
            {
                case 1: // 90° CCW (member.basisU = +group.basisV)
                    xform = new BlitTransform(
                        originU: (gUMax - mUMin) * _invUnionW,
                        originV: (gVMax - mVMin) * _invUnionH,
                        axisXU:  0f,
                        axisXV:  (mVMin - mVMax) * _invUnionH,
                        axisYU:  (mUMin - mUMax) * _invUnionW,
                        axisYV:  0f);
                    break;
                case 2: // 180° (member.basisU = -group.basisU)
                    xform = new BlitTransform(
                        originU: (gUMax - mUMax) * _invUnionW,
                        originV: (gVMax - mVMin) * _invUnionH,
                        axisXU:  (mUMax - mUMin) * _invUnionW,
                        axisXV:  0f,
                        axisYU:  0f,
                        axisYV:  (mVMin - mVMax) * _invUnionH);
                    break;
                case 3: // 270° CCW (member.basisU = -group.basisV)
                    xform = new BlitTransform(
                        originU: (gUMax - mUMax) * _invUnionW,
                        originV: (gVMax - mVMax) * _invUnionH,
                        axisXU:  0f,
                        axisXV:  (mVMax - mVMin) * _invUnionH,
                        axisYU:  (mUMax - mUMin) * _invUnionW,
                        axisYV:  0f);
                    break;
                default: // 0° (member.basisU = +group.basisU)
                    xform = new BlitTransform(
                        originU: (gUMax - mUMin) * _invUnionW,
                        originV: (gVMax - mVMax) * _invUnionH,
                        axisXU:  (mUMin - mUMax) * _invUnionW,
                        axisXV:  0f,
                        axisYU:  0f,
                        axisYV:  (mVMax - mVMin) * _invUnionH);
                    break;
            }
            _shader.ClearAndCopy(rc, postProcessed.SRgb, off, in xform);
        }
    }
}
