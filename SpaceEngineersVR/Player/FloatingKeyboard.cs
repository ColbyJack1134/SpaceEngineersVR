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
        private static readonly KeyboardWindow keyboard=new KeyboardWindow();
        private static MenuWindow window=>keyboard.Window;
        private static readonly WindowInteraction interaction=new WindowInteraction(keyboard.Window);
        private static DateTime lastUpdate;
        private sealed class HandState
        {
            public readonly KeyboardContact Touch=new KeyboardContact();
            public readonly KeyboardRepeat Repeat=new KeyboardRepeat();
            public readonly CockpitTouch.SurfaceHold Contact=new CockpitTouch.SurfaceHold();
            public bool DirectHeld;
            public int Hover=-1,Pressed=-1;
            public void Reset() { Touch.Reset(); Repeat.Reset(); Contact.Input.Reset(); DirectHeld=false; Hover=Pressed=-1; }
        }
        private static readonly HandState[] hands={ new HandState(),new HandState() };
        private static volatile SurfaceView current;
        private static bool placed;
        private static int dragHand;
        private static readonly RenderRecovery recovery=new RenderRecovery("Floating keyboard");
        private static bool failed => recovery.Failed;
        private static int hover=-1,pressed=-1;
        private static DateTime feedbackUntil;
        public static bool Available => !failed && MenuHands.Available;
        internal static bool Dragging => window.Drag!=0;
        private static Controller Hand(int index) => index==0 ? Player.HandR:Player.HandL;
        public static void Show(bool reposition,Vector3? screen=null)
        {
            var head=Player.Headset.pose.deviceToAbsolute.matrix;
            if(reposition || !placed || !keyboard.Reachable(head,screen)) keyboard.Place(head,screen);
            placed=true; ReleaseInput(); Publish();
        }
        public static void ReleaseInput() { foreach(var state in hands) state.Reset(); interaction.Reset(); hover=pressed=-1; lastUpdate=DateTime.UtcNow; }
        public static void Close() { ReleaseInput(); current=null; }
        internal static bool Hits(Matrix aim)
        {
            if(!window.Pointer(aim,out var point)) return false;
            var uv=window.UV(point);
            return uv.X>=0 && uv.X<=1 && uv.Y>=0 && uv.Y<=1 || window.Handle(point,true)!=0;
        }
        private static Matrix Aim(Controller hand)
        {
            if(Main.WorldAvailable && !ThirdPersonView.Active && TrackedArms.TryFreePointPose(hand,out var nativePoint))
                return (Matrix)(nativePoint*MatrixD.Invert(SpatialUi.DeviceWorld(Matrix.Identity)));
            return MenuHands.PointerTracking(false,hand);
        }
        public static void Update()
        {
            if(!MenuKeyboard.IsOpen) { Close(); return; }
            if(!Available) { MenuKeyboard.Close(); return; }
            if(InputRouter.Mode!=InputMode.Menu || !MenuPointer.GameFocused || !Player.Headset.pose.isTracked)
            { ReleaseInput(); current=null; return; }
            var now=DateTime.UtcNow; float seconds=(float)(now-lastUpdate).TotalSeconds; lastUpdate=now;
            if(interaction.Active)
            {
                foreach(var state in hands) { state.Touch.Reset(); state.Repeat.Reset(); state.Contact.Input.Reset(); }
                interaction.Update(Hand(dragHand),Aim(Hand(dragHand)),seconds);
                Publish(); return;
            }
            for(int i=0;i<2 && !interaction.Active;i++) if(UpdateHand(i,seconds)) break;
            hover=hands[0].Hover; pressed=hands[0].Pressed;
            if(MenuKeyboard.IsOpen) Publish();
        }
        private static bool UpdateHand(int index,float seconds)
        {
            var hand=Hand(index); var state=hands[index];
            int previousHover=state.Hover;
            state.Hover=state.Pressed=-1;
            if(!hand.pose.isTracked) { state.Reset(); return false; }
            Matrix aim=Aim(hand);
            Vector3 tip=aim.Translation;
            var local=window.Local(tip);
            Vector2 uv=window.UV(local);
            bool near=local.Z>=-.018f && local.Z<.07f && uv.X>=0 && uv.X<=1 && uv.Y>=0 && uv.Y<=1;
            Vector3 rayPoint=Vector3.Zero;
            bool ray=window.Pointer(aim,out rayPoint);
            Vector2 rayUv=window.UV(rayPoint);
            if(state.Contact.Input.Surface==null && interaction.Update(hand,aim,seconds))
            {
                if(interaction.Captured)
                {
                    dragHand=index;
                    foreach(var other in hands) { other.Touch.Reset(); other.Repeat.Reset(); other.Contact.Input.Reset(); }
                }
                state.Touch.Reset(); state.Repeat.Reset(); state.Contact.Input.Reset();
                return true;
            }
            var s=MakeView();
            int key=s.KeyAt(uv);
            if(near) state.Hover=key;
            if(ray && state.Hover<0) state.Hover=s.KeyAt(rayUv);
            MatrixD parent=Main.WorldAvailable && !ThirdPersonView.Active ? SpatialUi.DeviceWorld(window.Pose):(MatrixD)window.Pose;
            MatrixD wrist=Main.WorldAvailable && !ThirdPersonView.Active ? TrackedArms.FreeWristWorld(hand) : Alignment.Apply(Alignment.HandKey(hand),CockpitHandPose.GripWrist(hand.GripTracking));
            var localWrist=(Matrix)(wrist*MatrixD.Invert(parent));
            var input=state.Contact.Input;
            if(!input.Consumed) state.DirectHeld=near && key>=0;
            var action=InteractionInput.Read(hand,state.DirectHeld);
            input.Sample(true,action,state.Hover>=0 ? s.Id:null,state.Hover,
                reachable:input.Surface==null || !state.DirectHeld || state.Contact.Reachable(localWrist.Translation),guarded:state.Hover>=0,softCapture:false);
            if(input.Captured)
            {
                state.DirectHeld=near && key>=0;
                var bounds=s.Keys[input.Held].Bounds;
                state.Contact.Capture(localWrist,new Vector3((bounds.Center.X-.5f)*s.Width,(.5f-bounds.Center.Y)*s.Height,.001f));
            }
            int clicked=state.Touch.Update(local,near ? key:-1,input.Pressed ? input.Held:-1,action.Down || input.Consumed);
            state.Pressed=input.Committed ? input.Held:state.Touch.Held;
            if(input.Surface!=null) state.Hover=input.Held;
            clicked=state.Repeat.Update(clicked,MenuKeyboard.Repeatable(state.Pressed) ? state.Pressed:-1,DateTime.UtcNow);
            if(clicked>=0)
            {
                action.Consume();
                CockpitFeedback.Click(hand,amplitude:.45f,duration:.035f);
                feedbackUntil=DateTime.UtcNow.AddMilliseconds(60);
                MenuKeyboard.Activate(clicked);
            }
            else if(state.Hover>=0 && state.Hover!=previousHover && DateTime.UtcNow>=feedbackUntil) CockpitFeedback.Hover(hand);
            if(input.Consumed) action.Consume();
            return false;
        }
        private static SurfaceView MakeView() => new SurfaceView {
            Id="Keyboard",Style=SurfaceStyle.Keyboard,Pose=window.Pose,Width=window.Width,Height=window.Height,
            TrackingSpace=true,Text=MenuKeyboard.Preview,Keys=MenuKeyboard.Keys,
            Hover=hover>=0 || hands[1].Hover>=0 ? hover : MenuKeyboard.Selected,Pressed=pressed,HoverAlt=hands[1].Hover,PressedAlt=hands[1].Pressed,Handle=window.Drag,WindowHover=interaction.Hover };
        private static void Publish() { current=MakeView(); }
        internal static bool TryAttachment(Controller hand,out MatrixD pose,out Vector3D point,out float blend,bool tracking=false)
        {
            pose=MatrixD.Identity; point=Vector3D.Zero; blend=0;
            var state=hands[hand==Player.HandL ? 1:0];
            if(!MenuKeyboard.IsOpen || !state.DirectHeld) return false;
            bool world=Main.WorldAvailable && !ThirdPersonView.Active;
            if(!state.Contact.Attachment(world ? SpatialUi.DeviceWorld(window.Pose):(MatrixD)window.Pose,out pose,out point,out blend)) return false;
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
            if(failed || !MenuKeyboard.IsOpen || s==null) return;
            try
            {
                Matrix view=Matrix.Invert(OpenVR.System.GetEyeToHeadTransform(eye).ToMatrix()*Player.Headset.renderPose.deviceToAbsolute.matrix);
                float l=0,r=0,t=0,b=0; OpenVR.System.GetProjectionRaw(eye,ref l,ref r,ref t,ref b);
                // Match the menu controllers' projection/depth, independent of cockpit walls.
                FloatingSurface.Draw(target,new[] { s },view,VrMath.Projection(l,r,t,b,.03),MenuHands.Depth);
                WindowFrame.Draw(target,"Keyboard",new WindowFrame.Snapshot {Pose=(Matrix)s.Pose,Width=s.Width,Height=s.Height,Hover=s.WindowHover},view,VrMath.Projection(l,r,t,b,.03),ThirdPersonView.Active ? null:NativeHandLayer.Depth,MenuHands.Depth);
            }
            catch(Exception ex) { current=null; recovery.Fail(ex,"Floating keyboard renderer disabled"); }
        }
    }

    internal sealed class KeyboardRepeat
    {
        private int key=-1;
        private DateTime next;
        public void Reset() { key=-1; next=DateTime.MinValue; }
        public int Update(int clicked,int held,DateTime now)
        {
            if(clicked>=0)
            {
                key=clicked==held ? held:-1;
                next=now.AddSeconds(1);
                return clicked;
            }
            if(held!=key) Reset();
            if(key<0 || now<next) return -1;
            next=now.AddMilliseconds(100);
            return key;
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
