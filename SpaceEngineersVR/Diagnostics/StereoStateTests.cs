using System;
using SpaceEngineersVR.Player;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class StereoStateTests
    {
        public static void Run(Action<string> log)
        {
            var resolution=new Vector2I(1000,1000);
            var centered=(Matrix)VrMath.Projection(-1,1,-1,1,.03);
            var asymmetric=(Matrix)VrMath.Projection(-1.3f,.7f,-.8f,1.2f,.03);
            float center=StereoRenderState.QueryArea(new Vector3D(0,0,-10),1,0,Matrix.Identity,centered,resolution);
            float other=StereoRenderState.QueryArea(new Vector3D(0,0,-10),1,0,Matrix.Identity,asymmetric,resolution);
            Near(center,2500,"projected query area"); Near(other,center,"asymmetric projection changed size");
            Near(StereoRenderState.QueryArea(new Vector3D(8,0,-10),1,0,Matrix.Identity,asymmetric,resolution),center,"query used radial distance instead of view depth");
            Near(StereoRenderState.QueryArea(new Vector3D(0,0,-20),1,0,Matrix.Identity,centered,resolution),625,"distance scaling");
            for(int angle=0;angle<360;angle+=9)
            {
                var camera=MatrixD.CreateRotationY(MathHelper.ToRadians(angle))*MatrixD.CreateTranslation(1e7,2e7,-3e7);
                MatrixD inverse=MatrixD.Invert(MatrixD.Invert(camera)*(MatrixD)StereoRenderState.ShadowFrustum(1.3f,1.2f,.03,50000));
                foreach(double x in new[] {-1d,1d}) foreach(double y in new[] {-1d,1d}) foreach(double z in new[] {0d,1d})
                {
                    var point=Vector3D.Transform(new Vector3D(x,y,z),inverse);
                    if(!point.IsValid() || Vector3D.Distance(point,camera.Translation)>200000) throw new Exception("Cascade frustum unprojected to infinity");
                }
            }
            log("PASS stereo state: asymmetric flare query area, view-depth scaling and finite cascade bounds at large coordinates");
        }
        private static void Near(float actual,float expected,string message)
        { if(float.IsNaN(actual) || Math.Abs(actual-expected)>.1f) throw new Exception(message+": "+actual); }
    }
}
