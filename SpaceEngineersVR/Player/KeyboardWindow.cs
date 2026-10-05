using System;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    // Tracking-space window geometry and captured drag state, independent of GUI/SteamVR.
    internal sealed class KeyboardWindow
    {
        internal const float TopTrim=48f/640;
        public const float Aspect=9f/16*(1-TopTrim);
        internal readonly MenuWindow Window=new MenuWindow {Width=.62f,Aspect=Aspect,MinimumWidth=.42f,MaximumWidth=1};
        public Matrix Pose { get=>Window.Pose; set=>Window.Pose=value; }
        public float Width { get=>Window.Width; set=>Window.Width=value; }
        public float Height => Window.Height;
        public int Drag => Window.Drag;
        public void Place(Matrix head,Vector3? screen=null)
            => Window.Place(Facing(head,screen),Width,new Vector3(0,-.36f,-.55f),-.4f);
        public bool Reachable(Matrix head,Vector3? screen=null)
        {
            var origin=VrMath.TrackingOrigin(Facing(head,screen));
            var local=Vector3.Transform(Pose.Translation,Matrix.Invert(origin));
            return local.IsValid() && local.Length()<=1.2f && local.Z<-.15f && Math.Abs(local.X)<=-local.Z*.7f;
        }
        private static Matrix Facing(Matrix head,Vector3? screen)
        {
            if(!screen.HasValue) return head;
            var forward=screen.Value-head.Translation; forward.Y=0;
            if(!forward.IsValid() || forward.LengthSquared()<.01f) return head;
            head.Forward=Vector3.Normalize(forward);
            return head;
        }
        public void Begin(int kind,Matrix hand,Vector3 point) => Window.Begin(kind,hand,point);
        public bool Pointer(Matrix aim,out Vector3 point,bool captured=false) => Window.Pointer(aim,out point,captured);
        public Vector3 Local(Vector3 point,bool captured=false) => Window.Local(point,captured);
        public Vector2 UV(Vector3 point) => Window.UV(point);
        public void Move(Matrix hand,Vector3 point) => Window.Move(hand,point);
        public void Stop() => Window.Stop();
    }
}
