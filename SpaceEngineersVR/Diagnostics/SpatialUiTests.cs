using System;
using System.Linq;
using SpaceEngineersVR.Player;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class SpatialUiTests
    {
        private static void Require(bool value,string label) { if(!value) throw new Exception(label); }
        public static void Run(Action<string> log)
        {
            var touch=new SurfaceTouch();
            Require(touch.Update("keyboard",Vector3.Zero,0)<0,"A surface appearing through a hand fired a key");
            touch.Update("keyboard",new Vector3(0,0,.06f),0);
            Require(touch.Update("keyboard",new Vector3(0,0,.008f),0)==0,"Front poke missed");
            for(int i=0;i<300;i++) Require(touch.Update("keyboard",Vector3.Zero,0)<0,"Held poke repeated");
            Require(touch.Update("keyboard",Vector3.Zero,1)<0,"Sliding across keys fired");
            touch.Update("keyboard",new Vector3(0,0,.04f),1);
            Require(touch.Update("keyboard",Vector3.Zero,1)==1,"Retracted poke failed");
            Require(touch.Update("seat",Vector3.Zero,1)<0,"Surface ownership leaked");
            touch.Update("seat",new Vector3(0,0,.04f),1);
            Require(touch.Update("seat",new Vector3(0,0,-.05f),1)<0,"Deep/backside entry accepted");
            Require(touch.Update("seat",Vector3.Zero,1)<0,"Backside retraction fired a key");
            touch.Update("seat",new Vector3(float.NaN),1);
            Require(touch.Update("seat",Vector3.Zero,1)<0,"Tracking recovery auto-tapped");

            foreach(float frame in new[] { 1f/30,1f/60,1f/120 })
            {
                Vector3 offset=Vector3.Zero;
                for(int i=0;i<(int)Math.Round(1/frame);i++) offset=SeatFit.Step(offset,Vector3.Down,frame);
                Require(Math.Abs(offset.Y+.12)<.00001,"Seat speed depends on frame rate");
            }
            Require(SeatFit.Limit(new Vector3(float.NaN))==Vector3.Zero,"Invalid saved seat fit accepted");
            Require(SeatFit.Step(new Vector3(0,-.45f,0),Vector3.Down,2).Y==-.45f,"Seat lower bound exceeded");
            Require(HelmetHud.NearTemple(Matrix.CreateTranslation(.21f,0,0),Matrix.Identity),"Temple gesture missed");
            Require(!HelmetHud.NearTemple(Matrix.CreateTranslation(.21f,-.4f,-.2f),Matrix.Identity),"Chest pose toggles helmet");
            Require(!HandInteraction.ShowRay(0,false) && !HandInteraction.ShowRay(.04f,false),"Resting trigger shows the ray");
            Require(HandInteraction.ShowRay(.06f,false) && HandInteraction.ShowRay(.04f,true),"Light squeeze or hysteresis failed");
            Require(!HandInteraction.ShowRay(.02f,true) && !HandInteraction.ShowRay(float.NaN,true),"Ray remains after release/tracking loss");
            foreach(int side in new[] { -1,1 }) for(int i=0;i<=20;i++)
            {
                float fold=i/20f,height=MathHelper.Lerp(.07f,.40f*9/16,fold);
                MatrixD mount=MatrixD.CreateFromYawPitchRoll(.4,-.6,.2); mount.Translation=new Vector3D(1e8,2e8,-3e8);
                MatrixD opened=SpatialUi.WristPose(mount,fold,height,side);
                Vector3D bottom=opened.Translation-opened.Up*height*.5;
                Require(Vector3D.Distance(bottom,mount.Translation+mount.Right*side*.035)<1e-6,"Wrist long-edge hinge detached");
                Require(Vector3D.Dot(opened.Right,mount.Up*side)>.9999,"Wrist baseline does not follow forearm");
                if(i==20)
                {
                    Require(Vector3D.Dot(opened.Up,mount.Backward)>.9999,"Wrist opens into the arm");
                    Require(Vector3D.Dot(opened.Backward,mount.Right*side)>.9999,"Wrist opens away from chosen viewer side");
                }
            }
            foreach(string subtype in new[] { FighterProfile.Subtype,"OpenCockpitLarge" })
            {
                Require(SeatPanel.TryMount(subtype,out var mount,out float w,out float h) && mount.IsValid(),"Missing measured console mount");
                var panel=new SurfaceView { Width=w,Height=h,Keys=SeatPanel.Keys() };
                Require(panel.KeyAt(new Vector2(.5f,.42f))==4,"Seat reset is not at cross centre");
                Require(panel.KeyAt(new Vector2(.5f,.24f))==1 && panel.KeyAt(new Vector2(.5f,.60f))==6,"Seat fore/aft cross reversed");
                Require(panel.KeyAt(new Vector2(.19f,.42f))==3 && panel.KeyAt(new Vector2(.81f,.42f))==5,"Seat lateral cross reversed");
                Require(Vector3D.Dot(mount.Backward,Vector3D.Up)>.9,"Console controls face into the mesh");
            }
            Require(!SeatPanel.TryMount("unknown",out _,out _,out _),"Unmeasured cockpit gets a guessed floating panel");

            var wristMount=MatrixD.CreateFromYawPitchRoll(.4,-.6,.2);
            wristMount.Translation=new Vector3D(1e8,2e8,-3e8);
            foreach(int side in new[] { -1,1 })
            {
                Require(!SpatialUi.WristNeedsRefold(wristMount,wristMount.Translation+wristMount.Right*side*.3,side),"Facing wrist needlessly refolds");
                Require(!SpatialUi.WristNeedsRefold(wristMount,wristMount.Translation-wristMount.Right*side*.03,side),"Wrist edge-on jitter flips the screen");
                var viewer=wristMount.Translation-wristMount.Right*side*.3;
                Require(SpatialUi.WristNeedsRefold(wristMount,viewer,side),"Wrist remains inverted after viewer side changes");
                var reopened=SpatialUi.WristPose(wristMount,1,.225f,-side);
                Require(Vector3D.Dot(reopened.Backward,viewer-reopened.Translation)>0,"Reopened wrist still faces away");
            }

            var window=new KeyboardWindow();
            var headPose=Matrix.CreateFromYawPitchRoll(.7f,-.2f,.1f); headPose.Translation=new Vector3(2,1.5f,-3);
            window.Place(headPose);
            Require(window.Pose.IsValid() && Vector3.Dot(window.Pose.Backward,headPose.Translation-window.Pose.Translation)>0,"Keyboard starts facing away");
            var start=window.Pose; float initialWidth=window.Width;
            var controller=Matrix.CreateRotationY(.3f)*Matrix.CreateTranslation(1,1,-3);
            window.Begin(1,controller,Vector3.Zero); window.Move(controller,Vector3.Zero);
            Require(Vector3.Distance(window.Pose.Translation,start.Translation)<1e-5,"Grabbing keyboard snaps to controller");
            controller.Translation+=new Vector3(.2f,.1f,-.3f); window.Move(controller,Vector3.Zero);
            Require(Vector3.Distance(window.Pose.Translation,start.Translation+new Vector3(.2f,.1f,-.3f))<1e-5,"Captured keyboard does not follow hand");
            window.Stop(); var released=window.Pose; controller.Translation+=Vector3.One; window.Move(controller,Vector3.Zero);
            Require(window.Pose==released,"Keyboard moves after release");
            var corner=window.Pose.Translation-window.Pose.Right*window.Width/2+window.Pose.Up*window.Height/2;
            window.Begin(2,controller,Vector3.Zero);
            window.Move(controller,new Vector3(.2f,-.2f*KeyboardWindow.Aspect,0));
            Require(Math.Abs(window.Width-initialWidth-.2f)<1e-5,"Keyboard resize does not follow dragged corner");
            Require(Vector3.Distance(corner,window.Pose.Translation-window.Pose.Right*window.Width/2+window.Pose.Up*window.Height/2)<1e-5,"Resize moves opposite corner");
            window.Move(controller,new Vector3(100,-100,0)); Require(window.Width==1,"Keyboard exceeds upper size bound");
            window.Move(controller,new Vector3(-100,100,0)); Require(window.Width==.42f,"Keyboard exceeds lower size bound");
            window.Stop(); window.Place(headPose);
            var front=Matrix.CreateTranslation(0,0,.4f)*window.Pose;
            Require(window.Pointer(front,out var point) && point.Length()<1e-5,"Keyboard pointer plane mismatch");
            Require(!window.Pointer(Matrix.CreateTranslation(0,0,-.1f)*window.Pose,out _),"Backside ray manipulates keyboard");
            Require(KeyboardWindow.Handle(new Vector2(.5f,.955f))==1 && KeyboardWindow.Handle(new Vector2(.97f,.97f))==2,"Window handles unreachable");

            for(int i=0;i<100;i++)
            {
                Matrix neutral=Matrix.CreateFromYawPitchRoll(i*.02f,.2f,-.1f);
                Matrix captured=CockpitStickMath.GripPalm(false);
                Matrix turn=CockpitStickMath.RightVisual(new Vector3(.6f,-.4f,.3f));
                Matrix wrist=captured*turn;
                Vector3 palm=Vector3.Transform(new Vector3(-.105f,-.035f,0),wrist);
                Require(Vector3.Distance(palm,Vector3.Transform(FighterProfile.RightContact,turn))<1e-5,"Grasp separated from stick");
                Matrix head=Matrix.CreateRotationY(i*.07f); head.Translation=new Vector3(1,1.4f,-2);
                Matrix hand=Matrix.CreateTranslation(.3f,1.1f,-2.4f);
                var wheel=ToolbarWheel.HandPose(hand,head);
                Require(wheel.IsValid() && Math.Abs(wheel.Determinant()-1)<1e-5,"Hand wheel basis invalid");
                Require(Vector3.Dot(wheel.Backward,Vector3.Normalize(head.Translation-wheel.Translation))>.999f,"Wheel faces away");
            }
            foreach(float y in new[] { -.25f,-.45f,-.65f }) foreach(float z in new[] { -.4f,-.6f,-.8f })
            {
                var hand=Matrix.CreateTranslation(.3f,y,z);
                var wheel=ToolbarWheel.HandPose(hand,Matrix.Identity);
                Vector3 local=Vector3.Transform(hand.Translation,Matrix.Invert(wheel));
                Require(local.Y<-.30f,"Right hand overlaps bottom of the radial menu");
            }
            var keys=MenuKeyboard.MakeKeys(false);
            Require(keys.Length==46 && keys.Any(k=>k.Label=="DONE") && keys.Any(k=>k.Label=="BKSP"),"Keyboard layout incomplete");
            foreach(var key in keys) Require(KeyboardWindow.Handle(key.Bounds.Center)==0,"A key overlaps a window handle");
            var surface=new SurfaceView { Keys=keys,Width=.5f,Height=.3f,Pose=MatrixD.CreateTranslation(1e9,2e9,-3e9) };
            foreach(var key in keys)
            {
                var b=key.Bounds;
                Vector3 local=new Vector3((b.X+b.Width/2-.5f)*surface.Width,(.5f-b.Y-b.Height/2)*surface.Height,.006f);
                Vector3D world=Vector3D.Transform(local,surface.Pose);
                Require(surface.KeyAt(PhysicalSurface.UV(surface,PhysicalSurface.Point(surface,world)))==Array.IndexOf(keys,key),"Rendered key/touch mismatch at large coordinates");
            }
            log("PASS spatial input: front-only/retracted pokes, bounded seat fit, temple exclusion, light-trigger ray hysteresis, forearm hinge and inverted-wrist recovery, captured keyboard move/release/bounded resize, console arrow mappings, hand/wheel clearance, attached grasps, all 46 key touch volumes at large coordinates");
        }
    }
}
