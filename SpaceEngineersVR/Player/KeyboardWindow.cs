using System;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    // Tracking-space window geometry and captured drag state, independent of GUI/SteamVR.
    internal sealed class KeyboardWindow
    {
        internal const float TopTrim=48f/640;
        public const float Aspect=9f/16*(1-TopTrim);
        public Matrix Pose=Matrix.Identity;
        public float Width=.62f;
        public float Height => Width*Aspect;
        public int Drag { get; private set; }
        private Matrix startPose,relative;
        private Vector3 startPoint;
        private float startWidth;
        public void Place(Matrix head,Vector3? screen=null)
        {
            Stop();
            Pose=Matrix.CreateRotationX(-.4f)*Matrix.CreateTranslation(0,-.36f,-.55f)*VrMath.TrackingOrigin(Facing(head,screen));
        }
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
        public static int Handle(Vector2 uv)
        {
            if(uv.X>=.36f && uv.X<=.64f && uv.Y>=.92f && uv.Y<=.99f) return 1;
            if(uv.X>=.92f && uv.X<=1 && uv.Y>=.92f && uv.Y<=1) return 2;
            return 0;
        }
        public void Begin(int kind,Matrix hand,Vector3 point)
        {
            if(kind<1 || kind>2 || !hand.IsValid() || !point.IsValid()) return;
            Drag=kind; startPose=Pose; startWidth=Width; startPoint=point;
            relative=Pose*Matrix.Invert(hand);
        }
        public bool Pointer(Matrix aim,out Vector3 point,bool captured=false)
        {
            var local=aim*Matrix.Invert(captured ? startPose : Pose);
            point=Vector3.Zero;
            if(!local.IsValid() || local.Translation.Z<=0 || local.Forward.Z>=-.0001f) return false;
            float distance=-local.Translation.Z/local.Forward.Z;
            if(distance>2) return false;
            point=local.Translation+local.Forward*distance;
            return point.IsValid();
        }
        public Vector3 Local(Vector3 point,bool captured=false) => Vector3.Transform(point,Matrix.Invert(captured ? startPose : Pose));
        public Vector2 UV(Vector3 local) => new Vector2(.5f+local.X/Width,.5f-local.Y/Height);
        public void Move(Matrix hand,Vector3 point)
        {
            if(!hand.IsValid() || !point.IsValid()) { Stop(); return; }
            if(Drag==1) Pose=VrMath.Affine(relative*hand);
            else if(Drag==2)
            {
                var delta=point-startPoint;
                Width=MathHelper.Clamp(startWidth+(delta.X-delta.Y*Aspect)/(1+Aspect*Aspect),.42f,1.0f);
                Pose=startPose;
                // Anchor the opposite corner while maintaining the key aspect ratio.
                Pose.Translation+=Pose.Right*(Width-startWidth)/2-Pose.Up*((Width-startWidth)*Aspect/2);
            }
        }
        public void Stop() { Drag=0; }
    }
}
