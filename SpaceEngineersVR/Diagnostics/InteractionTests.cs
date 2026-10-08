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
                Require(w.Handle(new Vector3(0,-w.Height/2-w.BarOffset,0))==1,"Menu drag bar cannot be reached");
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
            dragged.Begin(1,hand,new Vector3(.05f,-dragged.Height/2-dragged.BarOffset,0));
            dragged.Move(hand,Vector3.Zero,Vector2.One,1f/60);
            Near(dragged.Pose.Translation,before.Translation,"Held scroll moved newly grabbed menu");
            dragged.Move(hand,Vector3.Zero,Vector2.Zero,1f/60);
            for(int i=0;i<60;i++) dragged.Move(hand,Vector3.Zero,new Vector2(0,-1),1f/60);
            Near(dragged.Pose.Translation,before.Translation+hand.Backward*.8f,"Stick pull did not bring menu closer along hand ray");
            var bar=Vector3.Transform(new Vector3(0,-dragged.Height/2-dragged.BarOffset,0),dragged.Pose);
            for(int i=0;i<60;i++) dragged.Move(hand,Vector3.Zero,new Vector2(1,0),1f/60);
            Require(Math.Abs(dragged.Width-MenuWindow.MaxWidth)<.0003f,"Horizontal stick did not enlarge menu");
            Near(Vector3.Transform(new Vector3(0,-dragged.Height/2-dragged.BarOffset,0),dragged.Pose),bar,"Stick resize moved the grabbed bar");
            var nudged=dragged.Pose;
            dragged.Move(hand,Vector3.Zero,new Vector2(.1f,-.1f),1f/60);
            Near(dragged.Pose.Translation,nudged.Translation,"Menu drifts inside thumbstick deadzone");
            for(int i=0;i<1000;i++) dragged.Move(hand,Vector3.Zero,new Vector2(0,-1),1f/60);
            Require(Math.Abs((dragged.Pose*Matrix.Invert(hand)).Translation.Z+.35f)<.0003f,"Menu could be pulled through hand");
            for(int i=0;i<1000;i++) dragged.Move(hand,Vector3.Zero,new Vector2(1,1),1f/60);
            var limit=(dragged.Pose*Matrix.Invert(hand)).Translation;
            Require(Math.Abs(limit.X-(before*Matrix.Invert(hand)).Translation.X)<.0003f && Math.Abs(limit.Z+3.5f)<.0003f && dragged.Width==dragged.MaximumWidth,"Menu stick resize/depth exceeded bounds");
            for(int i=0;i<1000;i++) dragged.Move(hand,Vector3.Zero,new Vector2(-1,0),1f/60);
            Require(dragged.Width==dragged.MinimumWidth,"Horizontal stick did not shrink to minimum");
            dragged.Cancel(); Require(dragged.Width==MenuWindow.DefaultWidth,"Cancelled stick resize retained changed width"); Near(dragged.Pose.Translation,before.Translation,"Interrupted stick nudge did not restore menu");
            foreach(var grip in new[] {new Vector3(.2f,-MenuWindow.DefaultWidth*9f/32-.035f,0),new Vector3(.4f,.2f,0)})
            {
                var held=new MenuWindow(); held.Place(Matrix.Identity);
                var palm=Matrix.CreateTranslation(Vector3.Transform(grip,held.Pose)+new Vector3(0,0,.01f));
                held.Begin(1,palm,grip,true);
                var anchor=Vector3.Transform(grip,held.Pose);
                held.Move(palm,Vector3.Zero,Vector2.Zero,1f/60);
                for(int i=0;i<30;i++) held.Move(palm,Vector3.Zero,new Vector2(0,1),1f/60);
                Near(Vector3.Transform(held.GrabPoint,held.Pose),anchor,"Hand-grab stick pushed the window");
                for(int i=0;i<30;i++) held.Move(palm,Vector3.Zero,new Vector2(1,0),1f/60);
                Require(held.Width>MenuWindow.DefaultWidth,"Hand-grab stick did not scale");
                Near(Vector3.Transform(held.GrabPoint,held.Pose),anchor,"Scaling moved the held point away from the hand");
                if(grip.Y<0) Near(held.GrabPoint,new Vector3(grip.X,-held.Height/2-held.BarOffset,0),"Held bar point left the bar while scaling");
            }
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
            foreach(string subtype in new[] { CockpitLayout.Fighter,CockpitLayout.ControlSeat })
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
            CockpitCaptureTests.Run(log);
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
            WeaponTests.Run(log);
            var seatKeys=SeatPanel.Keys(true,true);
            var seatView=new SurfaceView { Keys=seatKeys };
            Require(seatKeys.Length==14,"Seat ship controls or adjustment controls missing");
            for(int i=0;i<seatKeys.Length;i++)
            {
                var b=seatKeys[i].Bounds;
                if(b.Width<=0) continue;
                Require(seatView.KeyAt(new Vector2(b.X+b.Width/2,b.Y+b.Height/2))==i,"Seat controls overlap the new lock");
            }
            seatView.Width=.108f; seatView.Height=.120f; seatView.Pose=MatrixD.Identity;
            for(int i=0;i<seatKeys.Length;i++)
            {
                var bounds=seatKeys[i].Bounds;
                var pose=MatrixD.CreateTranslation((bounds.Center.X-.5)*seatView.Width,(.5-bounds.Center.Y)*seatView.Height,.3);
                Require(SpatialUi.WristRayTarget(new[] {seatView},pose,3,out int key,out _)==seatView && key==i,
                    "Seat ray does not target the same control as physical touch");
            }
            Require(SpatialUi.WristRayTarget(new[] {seatView},MatrixD.CreateTranslation(1,0,.3),3,out _,out _)==null,"Seat ray captures an unrelated surface");
            Require(SpatialUi.WristRayTarget(new[] {seatView},MatrixD.CreateTranslation(0,0,4),3,out _,out _)==null,"Seat ray exceeds interaction distance");
            log("PASS seat panel ray: all 14 controls match physical layout, unrelated directions and out-of-range rays remain free.");
            seatView.Keys=SeatPanel.Keys(true,false);
            Require(seatView.KeyAt(seatView.Keys[8].Bounds.Center)==8,"Locked cockpit cannot reset the seat");
            Require(seatView.Keys[8].Bounds==seatKeys[8].Bounds && seatView.Keys[12].Bounds==seatKeys[12].Bounds,"Locking sticks moved Reset or Lights");
            var config=new PluginConfig { ShipRollSensitivity=.77f,SeatFits=new[] { new SeatFitSetting { Subtype=CockpitLayout.Fighter,Y=.1f } },
                MenuWindows=new[] { new MenuWindowSetting { Screen="Inventory",Width=1.2f,Z=-1.4f,QW=1 },
                    new MenuWindowSetting { Screen="Remote/FighterCockpit",World="world-a",Cockpit=123,Width=.9f,Z=-.8f,QW=1 } },
                CockpitStates=new[] { new CockpitStateSetting { World="world-a",Cockpit=123,Covers=new[] {true,false,true} },
                    new CockpitStateSetting { World="world-b",Cockpit=123,Covers=new[] {false,true,false} } } };
            var serializer=new XmlSerializer(typeof(PluginConfig));
            using(var writer=new StringWriter())
            {
                serializer.Serialize(writer,config); var copy=(PluginConfig)serializer.Deserialize(new StringReader(writer.ToString()));
                Require(copy.MenuWindows[0].Width==1.2f && copy.MenuWindows[0].Z==-1.4f && copy.ShipRollSensitivity==.77f && copy.SeatFits[0].Y==.1f,"Menu persistence changed existing calibration");
                Require(copy.MenuWindows[0].World==null && copy.MenuWindows[0].Cockpit==0 &&
                    copy.MenuWindows[1].World=="world-a" && copy.MenuWindows[1].Cockpit==123 && copy.MenuWindows[1].Width==.9f &&
                    copy.CockpitStates.Length==2 && copy.CockpitStates[0].World=="world-a" && copy.CockpitStates[1].World=="world-b" &&
                    copy.CockpitStates[0].Cockpit==123 && copy.CockpitStates[1].Cockpit==123 &&
                    copy.CockpitStates[0].Covers[0] && !copy.CockpitStates[1].Covers[0],"Cockpit persistence lost world, seat, cover or legacy window identity");
            }
            log("PASS interaction regression checks: menu drag/resize/cancel across four aspects; thumbstick direction/deadzone/release/bounds; rounded/localized zero speed; independent cockpit levers/keys; touch/owner release gates; 200 moving LCD coordinate checks; native weapon fallback, compositor menu selection, moving model control hits and seat lock/reset; menu/config round trip.");
        }
        private static void ToolbarShortcuts(Action<string> log)
        {
            var jumpHold=new JumpHold(); var holdTime=DateTime.UtcNow;
            Require(!jumpHold.Update(true,true,true,holdTime),"Jump press toggles jetpack immediately");
            Require(!jumpHold.Update(true,false,false,holdTime.AddSeconds(.2)),"Short jump toggles jetpack");
            jumpHold.Update(true,true,true,holdTime.AddSeconds(1));
            Require(jumpHold.Update(true,false,true,holdTime.AddSeconds(1.51)),"Jump hold did not toggle jetpack");
            Require(!jumpHold.Update(true,false,true,holdTime.AddSeconds(5)),"Jump hold toggles jetpack repeatedly");
            jumpHold.Update(true,false,false,holdTime.AddSeconds(6));
            jumpHold.Update(true,true,true,holdTime.AddSeconds(7));
            Require(jumpHold.Update(true,false,true,holdTime.AddSeconds(7.51)),"Fresh jump hold cannot toggle jetpack off");
            jumpHold.Update(true,true,true,holdTime.AddSeconds(8)); jumpHold.Reset();
            Require(!jumpHold.Update(true,false,true,holdTime.AddSeconds(9)),"Context change resumes a held jetpack shortcut");
            Require(!jumpHold.Update(false,true,true,holdTime.AddSeconds(10)),"Unavailable character toggles jetpack");
            jumpHold.Reset();
            jumpHold.Update(true,true,true,holdTime,alternate:true);
            Require(!jumpHold.Update(true,false,false,holdTime.AddSeconds(.1)) && jumpHold.Tapped && jumpHold.Alternate,"Flight tap lost captured auto-dampener modifier");
            Require(!jumpHold.Update(true,false,false,holdTime.AddSeconds(.2)) && !jumpHold.Tapped,"Released stick repeated dampeners");
            jumpHold.Update(true,true,true,holdTime);
            Require(jumpHold.Update(true,false,true,holdTime.AddSeconds(.51)),"Jetpack hold failed");
            jumpHold.Update(true,false,false,holdTime.AddSeconds(.6));
            Require(!jumpHold.Tapped,"Jetpack hold release also toggled dampeners");
            jumpHold.Update(true,true,true,holdTime,holdAction:false);
            Require(!jumpHold.Update(true,false,true,holdTime.AddSeconds(1),holdAction:false),"Seated stick hold toggled jetpack");
            jumpHold.Update(true,false,false,holdTime.AddSeconds(2),holdAction:false);
            Require(!jumpHold.Tapped,"Long seated hold became a dampener tap");
            foreach(var method in Patches.DoubleClickTolerancePatch.Targets)
            {
                var patched=Patches.DoubleClickTolerancePatch.Transpiler(HarmonyLib.PatchProcessor.GetOriginalInstructions(method),method).ToList();
                Require(patched.Count(c => c.operand is float value && value==Patches.DoubleClickTolerancePatch.Tolerance)==1 &&
                    !patched.Any(c => c.operand is float value && value==Patches.DoubleClickTolerancePatch.Native),"Double-click tolerance missed "+method.DeclaringType.Name);
            }
            var taps=new DoubleTap(); double window=DoubleTap.Window;
            Require(taps.Update(true,false,false,true,holdTime)==0 && taps.Update(true,false,false,false,holdTime.AddSeconds(window*.8))==0,"Single dampener tap fired before the double-click window");
            Require(taps.Update(true,false,false,false,holdTime.AddSeconds(window+.01))==1 && taps.Update(true,false,false,false,holdTime.AddSeconds(window+.3))==0,"Single dampener tap lost or repeated");
            taps.Update(true,false,false,true,holdTime);
            Require(taps.Update(true,true,true,false,holdTime.AddSeconds(window*.8))==0 && taps.Update(true,false,true,false,holdTime.AddSeconds(window+.2))==0,"Second press toggled dampeners");
            Require(taps.Update(true,false,false,true,holdTime.AddSeconds(window+.25))==2 && taps.Update(true,false,false,false,holdTime.AddSeconds(1))==0,"Double click did not select auto dampeners once");
            taps.Update(true,false,false,true,holdTime);
            taps.Update(true,true,true,false,holdTime.AddSeconds(window/2));
            Require(taps.Update(true,false,false,false,holdTime.AddSeconds(1))==0 && taps.Update(true,false,false,false,holdTime.AddSeconds(2))==0,"Second press held past a tap still toggled dampeners");
            taps.Update(true,false,false,true,holdTime);
            Require(taps.Update(true,true,true,false,holdTime.AddSeconds(window+.05))==1,"Slow second press joined a double click");
            taps.Update(true,false,false,true,holdTime);
            Require(taps.Update(false,false,false,false,holdTime.AddSeconds(window/2))==0 && taps.Update(true,false,false,false,holdTime.AddSeconds(window+.3))==0,"Leaving flight kept a pending dampener tap");
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
            gesture.Update(true,true,true,false,cockpit,-1,now,true);
            Require(gesture.Update(true,false,false,true,cockpit,-1,now.AddSeconds(.1),true)==ToolbarGesture.Action.FirstPerson,"Third-person B tap does not return to first person");
            gesture.Update(true,true,true,false,cockpit,-1,now,true);
            Require(gesture.Update(true,false,true,false,cockpit,-1,now.AddSeconds(.3),true)==ToolbarGesture.Action.OpenWheel,"Third-person B hold lost radial menu");
            Require(gesture.Update(true,false,false,true,cockpit,-1,now.AddSeconds(.4),true)==ToolbarGesture.Action.None,"Radial release also exits third person");
            foreach(int interruption in new[] {0,1,2})
            {
                gesture.Update(true,true,true,false,cockpit,-1,now,true);
                gesture.Update(interruption!=0,false,true,false,interruption==1 ? new object() : cockpit,-1,now.AddSeconds(.05),interruption!=2);
                Require(gesture.Update(true,false,false,true,cockpit,-1,now.AddSeconds(.1),true)==ToolbarGesture.Action.None,"Interrupted B tap still changes camera");
            }
            gesture.Update(true,true,true,false,cockpit,-1,now,alternate:true);
            Require(gesture.Update(true,false,false,true,cockpit,-1,now.AddSeconds(.1))==ToolbarGesture.Action.Unequip && gesture.Alternate,"Modified Y tap lost its press-time modifier");
            gesture.Update(true,true,true,false,cockpit,-1,now,alternate:true);
            Require(gesture.Update(true,false,true,false,cockpit,-1,now.AddSeconds(.3),alternate:true)==ToolbarGesture.Action.OpenWheel && !gesture.Alternate,"Modified Y hold toggles dampeners instead of opening wheel");
            Require(gesture.Update(true,false,false,true,cockpit,-1,now.AddSeconds(.4))==ToolbarGesture.Action.None,"Modified wheel release also toggles dampeners");
            gesture.Update(true,true,true,false,cockpit,-1,now,alternate:true);
            gesture.Update(false,false,true,false,cockpit,-1,now.AddSeconds(.05));
            Require(gesture.Update(true,false,false,true,cockpit,-1,now.AddSeconds(.1))==ToolbarGesture.Action.None && !gesture.Alternate,"Modified Y tap escaped focus cancellation");
            log("PASS modified Y tap/hold/release/focus gates and B gestures: switch assignment, third-person return, hold-B radial and owner/menu/camera cancellation.");
        }
    }
}
