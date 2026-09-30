using System;
using SharpDX.Direct3D11;
using SpaceEngineersVR.Plugin;
using SpaceEngineersVR.Util;
using Valve.VR;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal static class FloatingKeyboard
    {
        private static readonly KeyboardWindow window=new KeyboardWindow();
        private static readonly SurfaceTouch touch=new SurfaceTouch();
        private static volatile SurfaceView current;
        private static bool placed,failed,directDrag;
        private static int hover=-1,pressed=-1;
        public static bool Available => !failed && MenuHands.Available;
        public static void Show(bool reposition)
        {
            var head=Player.Headset.pose.deviceToAbsolute.matrix;
            if(reposition || !placed || Vector3.Distance(head.Translation,window.Pose.Translation)>1.5f) window.Place(head);
            placed=true; ReleaseInput(); Publish();
        }
        public static void ReleaseInput() { touch.Reset(); window.Stop(); hover=pressed=-1; }
        public static void Close() { ReleaseInput(); current=null; }
        public static void Update()
        {
            if(!MenuKeyboard.IsOpen) { Close(); return; }
            if(!Available) { MenuKeyboard.Close(); return; }
            if(InputRouter.Mode!=InputMode.Menu || !MenuPointer.GameFocused || !Player.Headset.pose.isTracked || !Player.HandR.pose.isTracked)
            { ReleaseInput(); current=null; return; }
            var c=Controls.Static; Matrix aim=Player.HandR.AimTracking;
            Vector3 tip=aim.Translation+aim.Forward*.025f;
            if(window.Drag!=0)
            {
                touch.Reset();
                if(!c.Primary.RawPressed) window.Stop();
                else
                {
                    Vector3 point=window.Local(tip,true);
                    if(directDrag || window.Drag==1 || window.Pointer(aim,out point,true)) window.Move(aim,point);
                    c.Primary.BlockUntilRelease();
                }
                Publish(); return;
            }
            hover=pressed=-1;
            var local=window.Local(tip);
            Vector2 uv=window.UV(local);
            bool near=local.Z>=-.018f && local.Z<.07f && uv.X>=0 && uv.X<=1 && uv.Y>=0 && uv.Y<=1;
            bool ray=window.Pointer(aim,out var rayPoint);
            Vector2 rayUv=window.UV(rayPoint);
            int handle=near ? KeyboardWindow.Handle(uv) : ray ? KeyboardWindow.Handle(rayUv) : 0;
            if(handle!=0 && c.Primary.HasPressed)
            {
                directDrag=near; window.Begin(handle,aim,near ? local : rayPoint);
                touch.Reset(); c.Primary.BlockUntilRelease(); Player.HandR.Vibrate(0,.022f,100,.28f);
            }
            else
            {
                var s=MakeView();
                int key=s.KeyAt(uv);
                int clicked=touch.Update("Floating keyboard",local,key);
                if(near) hover=key;
                pressed=touch.Held;
                if(ray && hover<0) hover=s.KeyAt(rayUv);
                if(c.Primary.HasPressed && hover>=0) { clicked=hover; pressed=hover; }
                if(clicked>=0)
                {
                    c.Primary.BlockUntilRelease(); Player.HandR.Vibrate(0,.022f,125,.28f);
                    MenuKeyboard.Activate(clicked);
                }
            }
            if(MenuKeyboard.IsOpen) Publish();
        }
        private static SurfaceView MakeView() => new SurfaceView {
            Id="Keyboard",Style=SurfaceStyle.Keyboard,Pose=window.Pose,Width=window.Width,Height=window.Height,
            TrackingSpace=true,Text=MenuKeyboard.Preview,Keys=MenuKeyboard.Keys,
            Hover=hover>=0 ? hover : MenuKeyboard.Selected,Pressed=pressed,Handle=window.Drag };
        private static void Publish() { current=MakeView(); }
        public static float PointerDistance(Matrix aim)
        {
            var s=current;
            if(s==null) return 3;
            var local=aim*(Matrix)MatrixD.Invert(s.Pose);
            if(local.Translation.Z>0 && local.Forward.Z<-.0001f)
                return Math.Min(3,-local.Translation.Z/local.Forward.Z);
            return 3;
        }
        public static void Draw(Texture2D target,EVREye eye)
        {
            var s=current;
            if(failed || !MenuKeyboard.IsOpen || s==null || !Player.HandR.renderPose.isTracked) return;
            try
            {
                Matrix view=Matrix.Invert(OpenVR.System.GetEyeToHeadTransform(eye).ToMatrix()*Player.Headset.renderPose.deviceToAbsolute.matrix);
                float l=0,r=0,t=0,b=0; OpenVR.System.GetProjectionRaw(eye,ref l,ref r,ref t,ref b);
                // Match the menu controllers' projection/depth, independent of cockpit walls.
                PhysicalSurface.Draw(target,new[] { s },view,VrMath.Projection(l,r,t,b,.03),MenuHands.Depth);
            }
            catch(Exception ex) { failed=true; current=null; Logger.Warning(ex,"Floating keyboard renderer disabled"); }
        }
    }
}
