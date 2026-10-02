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
        private static readonly KeyboardContact touch=new KeyboardContact();
        private static readonly CockpitTouch.SurfaceHold contact=new CockpitTouch.SurfaceHold();
        private static volatile SurfaceView current;
        private static bool placed,failed,directDrag,directHeld;
        private static int hover=-1,pressed=-1;
        private static DateTime feedbackUntil;
        public static bool Available => !failed && MenuHands.Available;
        public static void Show(bool reposition)
        {
            var head=Player.Headset.pose.deviceToAbsolute.matrix;
            if(reposition || !placed || Vector3.Distance(head.Translation,window.Pose.Translation)>1.5f) window.Place(head);
            placed=true; ReleaseInput(); Publish();
        }
        public static void ReleaseInput() { touch.Reset(); contact.Input.Reset(); directHeld=false; window.Stop(); hover=pressed=-1; }
        public static void Close() { ReleaseInput(); current=null; }
        public static void Update()
        {
            if(!MenuKeyboard.IsOpen) { Close(); return; }
            if(!Available) { MenuKeyboard.Close(); return; }
            if(InputRouter.Mode!=InputMode.Menu || !MenuPointer.GameFocused || !Player.Headset.pose.isTracked || !Player.HandR.pose.isTracked)
            { ReleaseInput(); current=null; return; }
            var c=Controls.Static; Matrix aim=MenuHands.PointerTracking();
            Vector3 tip=aim.Translation+aim.Forward*.025f;
            if(Main.WorldAvailable && !ThirdPersonView.Active && TrackedArms.TryFreePointPose(Player.HandR,out var nativePoint))
            {
                aim=(Matrix)(nativePoint*MatrixD.Invert(SpatialUi.DeviceWorld(Matrix.Identity)));
                tip=aim.Translation;
            }
            if(window.Drag!=0)
            {
                touch.Reset(); contact.Input.Reset();
                if(!c.Primary.RawPressed) window.Stop();
                else
                {
                    Vector3 point=window.Local(tip,true);
                    if(directDrag || window.Drag==1 || window.Pointer(aim,out point,true)) window.Move(aim,point);
                    c.Primary.BlockUntilRelease();
                }
                Publish(); return;
            }
            int previousHover=hover;
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
                touch.Reset(); contact.Input.Reset(); c.Primary.BlockUntilRelease(); CockpitFeedback.Engage(Player.HandR);
            }
            else
            {
                var s=MakeView();
                int key=s.KeyAt(uv);
                int clicked=-1;
                if(near) hover=key;
                if(ray && hover<0) hover=s.KeyAt(rayUv);
                MatrixD parent=Main.WorldAvailable && !ThirdPersonView.Active ? SpatialUi.DeviceWorld(window.Pose):(MatrixD)window.Pose;
                MatrixD wrist=Main.WorldAvailable && !ThirdPersonView.Active ? TrackedArms.FreeWristWorld(Player.HandR) : Alignment.Apply(Alignment.HandKey(Player.HandR),CockpitHandPose.GripWrist(Player.HandR.GripTracking));
                var localWrist=(Matrix)(wrist*MatrixD.Invert(parent));
                var input=contact.Input;
                input.Sample(true,c.PointerPressure.RawPosition.X,c.Primary.RawPressed,hover>=0 ? s.Id:null,hover,
                    reachable:input.Surface==null || !directHeld || contact.Reachable(localWrist.Translation),guarded:hover>=0,softCapture:false);
                if(input.Captured)
                {
                    directHeld=near && key>=0;
                    var bounds=s.Keys[input.Held].Bounds;
                    contact.Capture(localWrist,new Vector3((bounds.Center.X-.5f)*s.Width,(.5f-bounds.Center.Y)*s.Height,.001f));
                }
                clicked=touch.Update(local,near ? key:-1,input.Pressed ? input.Held:-1,c.Primary.RawPressed || input.Consumed);
                pressed=input.Committed ? input.Held:touch.Held;
                if(input.Surface!=null) hover=input.Held;
                if(clicked>=0)
                {
                    c.Primary.BlockUntilRelease();
                    CockpitFeedback.Click(Player.HandR,amplitude:.45f,duration:.035f);
                    feedbackUntil=DateTime.UtcNow.AddMilliseconds(60);
                    MenuKeyboard.Activate(clicked);
                }
                else if(hover>=0 && hover!=previousHover && DateTime.UtcNow>=feedbackUntil) CockpitFeedback.Hover(Player.HandR);
                if(input.Consumed) c.Primary.BlockUntilRelease();
            }
            if(MenuKeyboard.IsOpen) Publish();
        }
        private static SurfaceView MakeView() => new SurfaceView {
            Id="Keyboard",Style=SurfaceStyle.Keyboard,Pose=window.Pose,Width=window.Width,Height=window.Height,
            TrackingSpace=true,Text=MenuKeyboard.Preview,Keys=MenuKeyboard.Keys,
            Hover=hover>=0 ? hover : MenuKeyboard.Selected,Pressed=pressed,Handle=window.Drag };
        private static void Publish() { current=MakeView(); }
        internal static bool TryAttachment(out MatrixD pose,out Vector3D point,out float blend,bool tracking=false)
        {
            pose=MatrixD.Identity; point=Vector3D.Zero; blend=0;
            if(!MenuKeyboard.IsOpen || !directHeld) return false;
            bool world=Main.WorldAvailable && !ThirdPersonView.Active;
            if(!contact.Attachment(world ? SpatialUi.DeviceWorld(window.Pose):(MatrixD)window.Pose,out pose,out point,out blend)) return false;
            if(world && tracking)
            {
                var inverse=MatrixD.Invert(SpatialUi.DeviceWorld(Matrix.Identity));
                pose*=inverse; point=Vector3D.Transform(point,inverse);
            }
            return true;
        }
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
                PhysicalSurface.Draw(target,new[] { s },view,VrMath.Projection(l,r,t,b,.03),MenuHands.Depth,ThirdPersonView.Active ? null:NativeHandLayer.Depth);
            }
            catch(Exception ex) { failed=true; current=null; Logger.Warning(ex,"Floating keyboard renderer disabled"); }
        }
    }

    internal sealed class KeyboardContact
    {
        private readonly SurfaceTouch poke=new SurfaceTouch();
        private bool used,pokePending;
        public int Held => poke.Held;
        public void Reset() { poke.Reset(); used=pokePending=false; }
        public int Update(Vector3 point,int key,int triggerKey,bool triggerHeld)
        {
            if(!point.IsValid()) { Reset(); return -1; }
            if(point.Z>.027f && !triggerHeld) used=pokePending=false;
            if(triggerHeld)
            {
                poke.Reset();
                if(triggerKey<0) return -1;
                // Absorb the trigger that follows a poke; later fresh presses remain repeatable.
                bool duplicate=pokePending;
                used=true; pokePending=false;
                return duplicate ? -1:triggerKey;
            }
            int clicked=poke.Update("Keyboard",point,key);
            if(clicked<0 || used) return -1;
            used=pokePending=true;
            return clicked;
        }
    }
}
