#if SEVR_DIAGNOSTICS
using System;
using System.Collections.Generic;
using System.IO;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Wrappers;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class BuildPreviewRenderTests
    {
        internal static bool Saved { get; private set; }
        internal static void Render(string output)
        {
            if(Saved) return;
            var matrices=MyRender11.Environment_Matrices;
            var snapshot=matrices.Capture(); var size=MyRender11.Resolution;
            var target=MyManagers.RwTexturesPool.BorrowRtv("Building range preview",size.X,size.Y,SharpDX.DXGI.Format.R8G8B8A8_UNorm_SRgb);
            try
            {
                var box=new BoundingBoxD(new Vector3D(-1.25),new Vector3D(1.25));
                foreach(bool near in new[] {true,false})
                {
                    var head=new Vector3D(near ? 4:12,0,0);
                    bool allowed=PlacementControls.BuildReach(box,MatrixD.Identity,head,2.5f,5);
                    if(allowed!=near) throw new InvalidOperationException("Native preview fixture has invalid reach state");
                    var from=new Vector3D(7,5,9);
                    var view=MatrixD.CreateLookAt(from,Vector3D.Zero,Vector3D.Up); view.Translation=Vector3D.Zero;
                    matrices.CameraPosition=from; matrices.ViewProjectionAt0=(Matrix)(view*VrMath.Projection(-1.1f,1.1f,-.62f,.62f,.03));
                    using(var rtv=new SharpDX.Direct3D11.RenderTargetView(MyRender11.DeviceInstance,target.GetResource()))
                        MyRender11.DeviceInstance.ImmediateContext.ClearRenderTargetView(rtv,new SharpDX.Mathematics.Interop.RawColor4(.035f,.055f,.075f,1));
                    var triangles=new List<SymmetryPlanes.View>();
                    var corners=new Vector3D[8];
                    for(int i=0;i<corners.Length;i++) corners[i]=box.GetCorner(i);
                    var color=new Color(allowed ? Color.Green*.6f:Color.Red*.8f,1f);
                    for(int i=0;i<corners.Length;i++) for(int j=i+1;j<corners.Length;j++)
                    {
                        var delta=corners[j]-corners[i];
                        if(Math.Abs(delta.Length()-2.5)>1e-8) continue;
                        var width=Vector3D.Normalize(Vector3D.Cross(delta,from-(corners[i]+corners[j])*.5))*.015;
                        triangles.Add(new SymmetryPlanes.View(corners[i]-width,corners[j]-width,corners[j]+width,color));
                        triangles.Add(new SymmetryPlanes.View(corners[i]-width,corners[j]+width,corners[i]+width,color));
                    }
                    NativeGizmos.Draw(target.Instance,triangles.ToArray());
                    UiTests.Save(target.GetResource(),Path.Combine(output,"build-range-"+(allowed ? "valid":"red")+".png"));
                }
                Saved=true;
            }
            finally { target.Release(); matrices.Restore(snapshot); }
        }
    }
}
#endif
