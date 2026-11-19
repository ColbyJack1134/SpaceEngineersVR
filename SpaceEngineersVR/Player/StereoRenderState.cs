using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Threading;
using SpaceEngineersVR.Plugin;
using Valve.VR;
using VRage.FileSystem;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal static class StereoRenderState
    {
        public static long Frame { get; private set; }
        public static bool Active { get; private set; }
        public static int View { get; set; }=-1;
        internal static bool PhysicalEye => Active && !RemoteScene.Active && View>=0 && View<=1;
        internal static bool AdvanceScene => !RemoteScene.Active && (!Active || View==0);
        internal static double PixelSlopeX { get; private set; }
        internal static Matrix CenterProjection { get; private set; }
        public static MatrixD CenterView { get; private set; }
        public static Matrix ShadowProjection { get; private set; }
        private static int request,remaining;
        private static readonly ConcurrentQueue<string> trace=new ConcurrentQueue<string>();
        public static bool Tracing => remaining>0;
        public static void RequestTrace() => Interlocked.Exchange(ref request,1);
        public static void CancelFrame() { Active=false; View=-1; }
        public static void Begin(MatrixD center,double near,double far)
        {
            Frame++; Active=true; View=-1; CenterView=center;
            float x=0,y=0;
            PixelSlopeX=0;
            var raw=Vector4.Zero;
            foreach(EVREye eye in new[] {EVREye.Eye_Left,EVREye.Eye_Right})
            {
                float l=0,r=0,t=0,b=0;
                OpenVR.System.GetProjectionRaw(eye,ref l,ref r,ref t,ref b);
                PixelSlopeX+=(r-l)/4;
                raw+=new Vector4(l,r,t,b)*.5f;
                x=Math.Max(x,Math.Max(Math.Abs(l),Math.Abs(r)));
                y=Math.Max(y,Math.Max(Math.Abs(t),Math.Abs(b)));
            }
            // A centered, symmetric frustum covers both asymmetric eye frusta.
            // Extra angular margin covers eye separation for nearby shadow casters.
            ShadowProjection=ShadowFrustum(x,y,near,far);
            CenterProjection=FlareFrustum(raw,near);
            if(Interlocked.Exchange(ref request,0)!=0)
            {
                while(trace.TryDequeue(out _)) { }
                remaining=120;
                trace.Enqueue("frame,view,event,values");
            }
        }
        internal static Matrix FlareFrustum(Vector4 bounds,double near) =>
            (Matrix)VrMath.Projection(bounds.X,bounds.Y,bounds.Z,bounds.W,near);
        internal static Matrix ShadowFrustum(float x,float y,double near,double far)
        {
            // Native cascade fitting unprojects z=0 and z=1. An infinite far
            // projection makes those bounds non-finite even though rasterization works.
            return VrMath.Projection(-x-.15f,x+.15f,-y-.15f,y+.15f,near,Math.Max(10000,far));
        }
        public static void Record(string name,params double[] values)
        {
            if(!Tracing) return;
            trace.Enqueue(Frame+","+View+","+name+","+string.Join(",",Array.ConvertAll(values,v=>v.ToString("R",CultureInfo.InvariantCulture))));
        }
        public static void End()
        {
            bool rendered=Active; Active=false; View=-1;
            if(!rendered || remaining<=0 || --remaining!=0) return;
            try
            {
                string file=Path.Combine(MyFileSystem.UserDataPath,"SEVR-render-trace.csv");
                File.WriteAllLines(file,trace.ToArray());
                Logger.Info("Stereo trace saved: "+file);
            }
            catch(Exception ex) { Logger.Warning(ex,"Could not save stereo trace"); }
        }
        internal static float QueryArea(Vector3D relative,float size,float shift,Matrix view,Matrix projection,Vector2I resolution)
        {
            double distance=relative.Length();
            if(distance<=1e-6 || !relative.IsValid() || size<=0) return 1;
            relative-=relative*(shift/distance);
            double depth=-Vector3D.TransformNormal(relative,view).Z;
            if(depth<=1e-6) return 1;
            double width=size*Math.Abs(projection.M11)*resolution.X/(2*depth);
            double height=size*Math.Abs(projection.M22)*resolution.Y/(2*depth);
            double area=width*height;
            return double.IsNaN(area) || double.IsInfinity(area) ? 1 : (float)Math.Max(1,area);
        }
    }
}
