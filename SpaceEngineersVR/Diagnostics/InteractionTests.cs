using System;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using SpaceEngineersVR.Config;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Player.Control;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class InteractionTests
    {
        private static void Require(bool condition,string reason) { if(!condition) throw new Exception(reason); }
        private static void Near(Vector3 a,Vector3 b,string reason) => Require(Vector3.Distance(a,b)<.0003f,reason);
        public static void Run(Action<string> log)
        {
            foreach(float aspect in new[] { .4f,.5625f,.75f,1.5f })
            {
                var w=new MenuWindow { Aspect=aspect }; w.Place(Matrix.Identity);
                Require(w.Handle(Vector3.Zero)==0,"Native menu centre became a window handle");
                Require(w.Handle(new Vector3(0,-w.Height/2-.085f,0))==1,"Menu drag bar cannot be reached");
                var aim=Matrix.CreateTranslation(0,0,-.3f);
                Require(w.Pointer(aim,out var point),"Front-facing menu pointer failed");
                var original=w.Pose; w.Begin(1,aim,point);
                var moved=Matrix.CreateFromYawPitchRoll(.25f,.12f,-.1f); moved.Translation=new Vector3(.1f,.2f,-.4f);
                w.Move(moved,point);
                Near((w.Pose*Matrix.Invert(moved)).Translation,(original*Matrix.Invert(aim)).Translation,"Menu detached from drag hand");
                w.Cancel(); Require(w.Pose==original,"Interrupted menu drag failed to recover");
                Vector3 topLeft=Vector3.Transform(new Vector3(-w.Width/2,w.Height/2,0),w.Pose);
                w.Begin(2,aim,Vector3.Zero); w.Move(aim,new Vector3(.5f,-.25f,0));
                Near(Vector3.Transform(new Vector3(-w.Width/2,w.Height/2,0),w.Pose),topLeft,"Resize moved opposite menu corner");
                w.Move(aim,new Vector3(99,-99,0)); Require(w.Width==MenuWindow.MaxWidth,"Menu maximum size failed");
                w.Move(aim,new Vector3(-99,99,0)); Require(w.Width==MenuWindow.MinWidth,"Menu minimum size failed");
                w.Stop(); Require(w.Drag==0,"Released menu remains captured");
            }
            var dragged=new MenuWindow(); dragged.Place(Matrix.Identity);
            var hand=Matrix.CreateRotationY(.3f)*Matrix.CreateTranslation(.2f,.1f,-.3f);
            var before=dragged.Pose;
            dragged.Begin(1,hand,Vector3.Zero);
            dragged.Move(hand,Vector3.Zero,Vector2.One,1f/60);
            Near(dragged.Pose.Translation,before.Translation,"Held scroll moved newly grabbed menu");
            dragged.Move(hand,Vector3.Zero,Vector2.Zero,1f/60);
            for(int i=0;i<60;i++) dragged.Move(hand,Vector3.Zero,new Vector2(0,-1),1f/60);
            Near(dragged.Pose.Translation,before.Translation+hand.Backward*.8f,"Stick pull did not bring menu closer along hand ray");
            for(int i=0;i<60;i++) dragged.Move(hand,Vector3.Zero,new Vector2(1,0),1f/60);
            Near(dragged.Pose.Translation,before.Translation+hand.Backward*.8f+hand.Right*.8f,"Stick lateral move used wrong frame");
            var nudged=dragged.Pose;
            dragged.Move(hand,Vector3.Zero,new Vector2(.1f,-.1f),1f/60);
            Near(dragged.Pose.Translation,nudged.Translation,"Menu drifts inside thumbstick deadzone");
            for(int i=0;i<1000;i++) dragged.Move(hand,Vector3.Zero,new Vector2(0,-1),1f/60);
            Require(Math.Abs((dragged.Pose*Matrix.Invert(hand)).Translation.Z+.35f)<.0003f,"Menu could be pulled through hand");
            for(int i=0;i<1000;i++) dragged.Move(hand,Vector3.Zero,new Vector2(1,1),1f/60);
            var limit=(dragged.Pose*Matrix.Invert(hand)).Translation;
            Require(Math.Abs(limit.X-1.5f)<.0003f && Math.Abs(limit.Z+3.5f)<.0003f,"Menu stick travel exceeded lateral/far bounds");
            dragged.Cancel(); Near(dragged.Pose.Translation,before.Translation,"Interrupted stick nudge did not restore menu");
            dragged.Begin(2,hand,Vector3.Zero);
            dragged.Move(hand,Vector3.Zero,Vector2.Zero,1f/60);
            dragged.Move(hand,Vector3.Zero,Vector2.One,1f/60);
            Near(dragged.Pose.Translation,before.Translation,"Thumbstick moved menu during resize");
            Require(EssentialHud.SpeedSegments("0.0",.00001f)==0 && EssentialHud.SpeedSegments("0.00",.00001f)==0,
                "Zero speed readout still lights a segment for physics drift");
            Require(EssentialHud.SpeedSegments("1",.01f)==1 && EssentialHud.SpeedSegments("100",1)==11,
                "Nonzero speed gauge lost its segments");
            var culture=System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture=System.Globalization.CultureInfo.GetCultureInfo("de-DE");
                Require(EssentialHud.SpeedSegments("0,00",.00001f)==0,"Localized zero speed lights a segment");
            }
            finally { System.Globalization.CultureInfo.CurrentCulture=culture; }
            foreach(string subtype in new[] { FighterProfile.Subtype,CockpitLayout.ControlSeat })
                for(int i=0;i<CockpitLayout.Count(subtype);i++)
                {
                    var panel=CockpitButtons.Preview(subtype,i);
                    Require(panel.Keys.Length==1 && panel.KeyAt(new Vector2(.5f))==0,"Native control has no touch region");
                    Require(Math.Abs(panel.Pose.Determinant()-1)<.0001,"Cockpit control mount is not rigid");
                    var moving=MatrixD.CreateFromYawPitchRoll(.6,.3,-.2)*MatrixD.CreateTranslation(2e6,-3e6,4e6);
                    panel.Pose*=moving;
                    var target=Vector3D.Transform(new Vector3D(0,0,.01),panel.Pose);
                    Near(PhysicalSurface.Point(panel,target),new Vector3(0,0,.01f),"Moving cockpit lost control position");
                }
            var gate=new InputGate(); var touch=new SurfaceTouch();
            ToolbarShortcuts(log);
            SeatTrigger(log);
            gate.Update(true,false); gate.Update(true,true); Require(gate.Pressed,"Fresh cockpit/LCD press lost");
            gate.Block(); gate.Update(false,false); gate.Update(true,true); Require(!gate.Pressed,"Held button leaked after screen/owner change");
            Require(touch.Update("screen1",new Vector3(0,0,.01f),0)<0,"Unarmed touch activated on arrival");
            touch.Update("screen1",new Vector3(0,0,.04f),0);
            Require(touch.Update("screen1",new Vector3(0,0,.01f),0)==0,"Deliberate touch lost");
            Require(touch.Update("screen2",new Vector3(0,0,.01f),0)<0,"Touch crossed screen ownership without retraction");
            for(int i=0;i<200;i++)
            {
                var world=MatrixD.CreateFromYawPitchRoll(i*.03,i*.02,i*.01); world.Translation=new Vector3D(2e6,-3e6,4e6);
                var plane=TouchScreenBridge.PlaneFor(new Vector3(-.5f,.3f,0),new Vector3(-.5f,-.3f,0),new Vector3(.5f,-.3f,0),world);
                var local=PhysicalSurface.Point(plane,Vector3D.Transform(new Vector3D(.2,-.15,.04),world));
                Near(local,new Vector3(.2f,-.15f,.04f),"LCD coordinates depend on ship position/orientation");
                var uv=PhysicalSurface.UV(plane,local);
                Require(Vector2.Distance(uv,new Vector2(.7f,.75f))<.0001f,"LCD UV orientation flipped");
            }
            Require(TouchScreenBridge.PlaneFor(Vector3.Zero,Vector3.Zero,Vector3.One,MatrixD.Identity)==null,"Degenerate LCD accepted");
            Require(WeaponProfile.Find("WelderItem","Models/Weapons/Welder.mwm")==null &&
                WeaponProfile.Find("AngleGrinderItem","Models/Weapons/AngleGrinder.mwm")==null &&
                WeaponProfile.Find("SemiAutoPistolItem","Models/Weapons/Pistol_Warfare.mwm")==null,
                "Tools without support grips acquired a two-hand profile");
            Require(WeaponProfile.Find(WeaponProfile.Rifle.Item,WeaponProfile.Rifle.Model)==WeaponProfile.Rifle &&
                WeaponProfile.Find(WeaponProfile.Launcher.Item,WeaponProfile.Launcher.Model)==WeaponProfile.Launcher,"Accepted original weapon profiles lost");
            Require(!MenuWindow.StereoClient(false,true),"Ordinary native menu was resampled into eye texture");
            Require(MenuWindow.StereoClient(true,true),"Keyboard no longer has its accepted stereo occlusion path");
            var seatKeys=SeatPanel.Keys(true,true);
            var seatView=new SurfaceView { Keys=seatKeys };
            Require(seatKeys.Length==13,"Seat lock/reset missing");
            for(int i=0;i<seatKeys.Length;i++)
            {
                var b=seatKeys[i].Bounds;
                if(b.Width<=0) continue;
                Require(seatView.KeyAt(new Vector2(b.X+b.Width/2,b.Y+b.Height/2))==i,"Seat controls overlap the new lock");
            }
            // The same actual per-hand state machine drives left and right. A
            // held trigger/embedded finger cannot cross focus or surface ownership.
            var hands=new[] { new CockpitTouch.Hand(),new CockpitTouch.Hand() };
            foreach(var h in hands)
            {
                var away=new Vector3(0,0,.2f);
                Require(h.Sample(true,true,"seat",away,-1,7)<0,"Held trigger activated a newly available seat lock");
                h.Sample(true,false,"seat",away,-1,7);
                Require(h.Sample(true,true,"seat",away,-1,7)==7,"Fresh left/right ray press lost");
                for(int i=0;i<30;i++) Require(h.Sample(true,true,"seat",away,-1,7)<0 && h.Held==7,"Held seat arrow repeats clicks or loses hold");
                h.Sample(false,true,"seat",away,-1,7);
                Require(h.Sample(true,true,"seat",away,-1,7)<0,"Menu/tracking return generated a click");
                h.Sample(true,false,"seat",new Vector3(0,0,.04f),7,-1);
                Require(h.Sample(true,false,"seat",new Vector3(0,0,.01f),7,-1)==7,"Left/right fingertip poke lost");
                Require(h.Sample(true,false,"switch",new Vector3(0,0,.01f),0,-1)<0,"Embedded finger activated a new surface");
            }
            hands[0].Reset();
            Require(hands[1].Sample(true,false,"switch",new Vector3(0,0,.04f),0,-1)<0 &&
                hands[1].Sample(true,false,"switch",new Vector3(0,0,.01f),0,-1)==0,"One hand reset disabled the other");
            foreach(var contactHand in hands)
            {
                contactHand.Reset();
                var motion=new Vector3(.08f,-.03f,.01f);
                var panel=new SurfaceView { Width=.108f,Height=.15f,Keys=SeatPanel.Keys(true) };
                var centre=panel.Keys[5].Bounds.Center;
                var contact=new Vector3((centre.X-.5f)*panel.Width,(.5f-centre.Y)*panel.Height,.04f);
                contactHand.Sample(true,false,"Seat",contact,5,-1,seatMotion:motion);
                contact.Z=.005f;
                Require(contactHand.Sample(true,false,"Seat",contact,5,-1,seatMotion:motion)==5,"Seat physical press not acquired");
                for(int frame=0;frame<180;frame++)
                {
                    var shifted=motion+new Vector3(frame*.002f,-frame*.0002f,frame*.0001f);
                    var finger=contact+(shifted-motion); finger.Z-=.05f;
                    var local=contactHand.ContactPoint(finger,shifted);
                    int key=panel.KeyAt(PhysicalSurface.UV(panel,local));
                    Require(contactHand.Sample(true,false,"Seat",local,key,-1,seatMotion:shifted)<0 && contactHand.Held==5,
                        "Sustained seat push stopped after penetration or its own seat motion");
                }
                contactHand.Sample(true,false,"Seat",new Vector3(0,0,.04f),5,-1);
                Require(contactHand.Held<0,"Seat movement survives finger withdrawal");
                contactHand.Reset();
                contactHand.Sample(true,false,"cover",new Vector3(0,0,.04f),0,-1,triggerOnly:true);
                Require(contactHand.Sample(true,false,"cover",new Vector3(0,0,.005f),0,-1,triggerOnly:true)<0,
                    "Open cover closes from a finger push");
                Require(contactHand.Sample(true,true,"cover",new Vector3(0,0,.005f),0,-1,triggerOnly:true)==0,
                    "Near cover trigger click lost");
                Require(contactHand.Sample(true,true,"cover",new Vector3(0,0,.005f),0,0,triggerOnly:true)<0,
                    "Held cover trigger toggles repeatedly");
                contactHand.Sample(false,true,"cover",Vector3.Zero,0,0,triggerOnly:true);
                Require(contactHand.Sample(true,true,"cover",Vector3.Zero,0,0,triggerOnly:true)<0,"Cover tracking return accepts held trigger");
            }
            log("PASS sustained physical seat movement: 180-frame deep push with accumulated seat motion, withdrawal, both hands; cover closes only on fresh trigger.");
            var config=new PluginConfig { ShipRollSensitivity=.77f,SeatFits=new[] { new SeatFitSetting { Subtype=FighterProfile.Subtype,Y=.1f } },
                MenuWindows=new[] { new MenuWindowSetting { Screen="Inventory",Width=1.2f,Z=-1.4f,QW=1 } } };
            var serializer=new XmlSerializer(typeof(PluginConfig));
            using(var writer=new StringWriter())
            {
                serializer.Serialize(writer,config); var copy=(PluginConfig)serializer.Deserialize(new StringReader(writer.ToString()));
                Require(copy.MenuWindows[0].Width==1.2f && copy.MenuWindows[0].Z==-1.4f && copy.ShipRollSensitivity==.77f && copy.SeatFits[0].Y==.1f,"Menu persistence changed existing calibration");
            }
            log("PASS interaction regression checks: menu drag/resize/cancel across four aspects; thumbstick direction/deadzone/release/bounds; rounded/localized zero speed; independent cockpit levers/keys; touch/owner release gates; 200 moving LCD coordinate checks; native weapon fallback, compositor menu selection, moving model control hits and seat lock/reset; menu/config round trip.");
        }
        private static void ToolbarShortcuts(Action<string> log)
        {
            var gesture=new ToolbarGesture(); var cockpit=new object(); var now=DateTime.UtcNow;
            foreach(int slot in new[] {0,3,8,9,12})
            {
                Require(gesture.Update(true,true,true,false,cockpit,slot,now)==ToolbarGesture.Action.None,"B press opened assignment before tap/hold was known");
                Require(gesture.Update(true,false,false,true,cockpit,slot,now.AddSeconds(.1))==ToolbarGesture.Action.AssignSwitch && gesture.Switch==slot,
                    "B tap lost the highlighted switch, including the second page");
                gesture.Update(true,true,true,false,cockpit,slot,now);
                Require(gesture.Update(true,false,true,false,cockpit,slot,now.AddSeconds(.3))==ToolbarGesture.Action.OpenWheel,"Switch hover stole hold-B from the radial menu");
                Require(gesture.Update(true,false,false,true,cockpit,slot,now.AddSeconds(.4))==ToolbarGesture.Action.None,"Radial release also edited a switch");
            }
            gesture.Update(true,true,true,false,cockpit,-1,now);
            Require(gesture.Update(true,false,false,true,cockpit,-1,now.AddSeconds(.1))==ToolbarGesture.Action.Unequip,"Ordinary B tap changed");
            foreach(int next in new[] {-1,4})
            {
                gesture.Update(true,true,true,false,cockpit,3,now);
                Require(gesture.Update(true,false,false,true,cockpit,next,now.AddSeconds(.1))==ToolbarGesture.Action.None,"Lost/switched hover edited another switch or unequipped");
            }
            gesture.Update(true,true,true,false,cockpit,3,now);
            Require(gesture.Update(true,false,false,true,new object(),3,now.AddSeconds(.1))==ToolbarGesture.Action.None,"B tap crossed cockpit ownership");
            gesture.Update(true,true,true,false,cockpit,3,now);
            gesture.Update(false,false,true,false,cockpit,3,now);
            Require(gesture.Update(true,false,false,true,cockpit,3,now.AddSeconds(.1))==ToolbarGesture.Action.None,"Menu/focus interruption retained B assignment");
            gesture.Update(true,true,true,false,cockpit,3,now); gesture.Reset();
            Require(gesture.Update(true,false,false,true,cockpit,3,now.AddSeconds(.1))==ToolbarGesture.Action.None,"Closed wheel retained a pending tap");
            log("PASS switch assignment gesture: contextual tap, both pages, hold-B radial, hover loss and owner/focus/menu cancellation.");
        }
        private static void SeatTrigger(Action<string> log)
        {
            foreach(int rayKey in new[] {-1,9}) foreach(int button in new[] {0,5,7,10})
            {
                var hand=new CockpitTouch.Hand(); var pointer=new PointerIntent();
                var point=new Vector3(0,0,.04f);
                int target=CockpitTouch.Hand.SeatTarget(point,button,rayKey);
                pointer.Begin(true,0,false);
                hand.Sample(true,false,"Seat",point,button,-1);
                pointer.Begin(true,.1f,false,true,"Seat");
                Require(!pointer.Capture("CockpitControl0",true),"Distant switch stole input from nearby seat button");
                Require(pointer.Capture("Seat",target>=0),"Nearby finger did not capture the seat without a ray hit");
                pointer.Begin(true,.8f,true,true,"Seat");
                int clicked=hand.Sample(true,true,"Seat",point,button,pointer.Capture("Seat",true) ? target : -1);
                Require(clicked==button && hand.Hover==button && hand.Held==button,"Seat click differs from fingertip highlight when ray misses or points at power");
                for(int i=0;i<120;i++) Require(hand.Sample(true,true,"Seat",point,button,target)<0 && hand.Held==button,
                    "Held near trigger repeats a toggle or stops seat motion");
                hand.Sample(true,false,"Seat",point,button,-1);
                Require(hand.Held<0,"Near trigger release keeps moving seat");
                hand.Reset();
                Require(hand.Sample(true,true,"Seat",point,button,target)<0,"Tracking/menu return accepted held near trigger");
            }
            Require(CockpitTouch.Hand.SeatTarget(new Vector3(0,0,.2f),5,7)==7,"Far seat interaction lost its ray target");
            Require(CockpitTouch.Hand.SeatTarget(new Vector3(0,0,-.1f),5,-1)<0,"Finger behind panel activates a button");
            Require(CockpitTouch.Hand.SeatTarget(new Vector3(0,0,.04f),-1,7)==7,"Panel margin masks valid ray target");
            var movingHand=new CockpitTouch.Hand();
            var seat=new SurfaceView { Width=.108f,Height=.120f,Keys=SeatPanel.Keys(true) };
            var centre=seat.Keys[5].Bounds.Center;
            var finger=new Vector3((centre.X-.5f)*seat.Width,(.5f-centre.Y)*seat.Height,.04f);
            movingHand.Sample(true,false,"Seat",finger,5,-1);
            movingHand.Sample(true,true,"Seat",finger,5,5);
            for(int frame=0;frame<180;frame++)
            {
                var motion=new Vector3(frame*.002f,frame*.0002f,-frame*.0001f);
                var local=movingHand.ContactPoint(finger+motion,motion);
                int key=seat.KeyAt(PhysicalSurface.UV(seat,local));
                movingHand.Sample(true,true,"Seat",local,key,CockpitTouch.Hand.SeatTarget(local,key,-1),seatMotion:motion);
                Require(movingHand.Held==5,"Nearby trigger hold stopped due to its own seat motion");
            }
            movingHand.Sample(true,false,"Seat",finger,5,-1);
            Require(movingHand.Held<0 && movingHand.ContactPoint(finger,Vector3.One)==finger,"Released near trigger retains seat compensation");
            var toggle=new CockpitTouch.Hand(); var hover=new Vector3(0,0,.04f); var contact=new Vector3(0,0,.005f);
            toggle.Sample(true,false,"Seat",hover,9,-1);
            Require(toggle.Sample(true,true,"Seat",hover,9,9)==9,"Near power press lost");
            Require(toggle.Sample(true,true,"Seat",contact,9,9)<0,"Trigger then finger contact toggles power twice");
            toggle.Sample(true,false,"Seat",hover,9,-1);
            Require(toggle.Sample(true,false,"Seat",contact,9,-1)==9,"New physical press after retraction lost");
            Require(toggle.Sample(true,true,"Seat",contact,9,9)<0,"Physical press then trigger toggles power twice");
            log("PASS finger-first seat clicks: ray miss/conflict, cross-panel priority, held movement/toggle, release and tracking gates.");
        }
    }
}
