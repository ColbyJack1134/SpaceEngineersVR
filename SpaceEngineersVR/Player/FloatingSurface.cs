using System.Collections.Generic;
using SharpDX.Direct3D11;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal static class FloatingSurface
    {
        internal static void Draw(Texture2D target,IEnumerable<SurfaceView> surfaces,MatrixD view,MatrixD projection,ShaderResourceView controllerDepth=null)
            => PhysicalSurface.Draw(target,surfaces,view,projection,controllerDepth,ThirdPersonView.Active ? null:NativeHandLayer.Depth);
    }
}
