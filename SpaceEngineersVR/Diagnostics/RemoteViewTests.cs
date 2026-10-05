using System;
using System.Linq;
using System.IO;
using SharpDX.Direct3D11;
using System.Runtime.Serialization;
using HarmonyLib;
using Sandbox.Game.Entities;
using Sandbox.Game.Weapons;
using SpaceEngineers.Game.Entities.Blocks;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Player.Control;
using Valve.VR;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class RemoteViewTests
    {
        private static void Require(bool value,string message) { if(!value) throw new Exception(message); }
        private static T Entity<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
        private static void Set(object instance,string name,object value) => AccessTools.Field(instance.GetType(),name).SetValue(instance,value);
        private static void Near(MatrixD actual,MatrixD expected,string message)
        {
            Require(Vector3D.Distance(actual.Translation,expected.Translation)<.00001 &&
                Vector3D.Distance(actual.Right,expected.Right)<.00001 && Vector3D.Distance(actual.Up,expected.Up)<.00001,message);
        }
        internal static void PaintSource(OverlayCanvas source)
        {
                source.Clear(System.Drawing.Color.FromArgb(20,45,62));
                using(var pen=new System.Drawing.Pen(System.Drawing.Color.FromArgb(70,120,135),3))
                {
                    for(int x=0;x<1920;x+=120) source.Graphics.DrawLine(pen,x,0,x,1080);
                    for(int y=0;y<1080;y+=120) source.Graphics.DrawLine(pen,0,y,1920,y);
                    source.Graphics.DrawEllipse(System.Drawing.Pens.White,900,480,120,120);
                    source.Graphics.DrawLine(System.Drawing.Pens.White,920,540,1000,540);
                    source.Graphics.DrawLine(System.Drawing.Pens.White,960,500,960,580);
                }
        }
        internal static void Preview(Device device,string output)
        {
            using(var source=new OverlayCanvas("Camera fixture",1920,1080,1,false,device))
            using(var target=new OverlayCanvas("Play-space screen",1200,800,1,false,device))
            {
                PaintSource(source);
                source.Upload();
                using(var image=new ShaderResourceView(device,source.Texture))
                {
                    var remote=new RemoteView.View {Pose=MatrixD.CreateTranslation(0,1.5,-1.5),Width=1.4f,Height=1.4f*9/16,Hover=0};
                    foreach(bool lean in new[] {false,true})
                    {
                        target.Clear(System.Drawing.Color.FromArgb(8,14,21)); target.Upload();
                        MatrixD head=MatrixD.CreateTranslation(lean ? .22:0,lean ? 1.6:1.5,lean ? -.1:0);
                        RemoteFeed.DrawPanel(target.Texture,image,remote,MatrixD.Invert(head),VrMath.Projection(-.9f,.9f,-.6f,.6f,.03));
                        UiTests.Save(target.Texture,Path.Combine(output,"remote-window-"+(lean ? "lean":"center")+".png"));
                    }
                }
            }
            RemoteFeed.Reset();
        }
        public static void Run(Action<string> log)
        {
            var home=Entity<MyCockpit>(); var remote=Entity<MyRemoteControl>();
            var camera=Entity<MyCameraBlock>(); var alternate=Entity<MyCameraBlock>();
            var turret=(MyLargeTurretBase)FormatterServices.GetUninitializedObject(typeof(MyTurretControlBlock).Assembly.GetTypes().First(t=>!t.IsAbstract && typeof(MyLargeTurretBase).IsAssignableFrom(t))); var custom=Entity<MyTurretControlBlock>();
            Set(remote,"m_previousControlledEntity",home);
            Set(camera,"<IsWorking>k__BackingField",true); Set(alternate,"<IsWorking>k__BackingField",true);
            Set(turret,"<IsWorking>k__BackingField",true);
            Require(SeatPanel.RemoteOwner(remote,home)==home && SeatPanel.RemoteOwner(turret,home)==home &&
                SeatPanel.RemoteOwner(custom,home)==home && SeatPanel.RemoteOwner(home,home)==null,
                "Physical seat keypad targets remote controls or changes ordinary cockpit routing");
            Require(RemoteView.PreviousOwner(remote)==home && RemoteView.SeatFor(remote,null)==home,"Remote control lost its physical home cockpit");
            Require(RemoteView.Owner(remote,camera)==remote && RemoteView.Feed(remote,camera)==camera,"Camera stole remote input ownership");
            Require(RemoteView.Owner(remote,null)==remote && RemoteView.Feed(remote,null)==null,"Missing camera removed control or invented a feed");
            Require(RemoteView.Owner(home,camera)==camera && RemoteView.PreviousOwner(home)==home,"Ordinary camera changed flight owner");
            Require(RemoteView.Owner(turret,null)==turret && RemoteView.Feed(turret,null)==turret,"Native turret lost its own feed");
            Require(RemoteView.Owner(custom,camera)==custom && RemoteView.Feed(custom,camera)==camera,"Custom turret camera stole ownership");
            Require(RemoteView.ExitControlled(remote) && RemoteView.ExitControlled(turret) && RemoteView.ExitControlled(custom) &&
                !RemoteView.ExitControlled(home) && !RemoteView.ExitControlled(camera),"A exit would eject the home pilot or only exit the remote's camera");
            Set(camera,"<IsWorking>k__BackingField",false);
            Require(RemoteView.Feed(remote,camera)==null && RemoteView.Owner(remote,camera)==remote,"Camera power loss interrupted remote flight");
            Set(camera,"<IsWorking>k__BackingField",true);
            var closed=AccessTools.PropertySetter(typeof(VRage.Game.Entity.MyEntity),"Closed");
            var marked=AccessTools.PropertySetter(typeof(VRage.Game.Entity.MyEntity),"MarkedForClose");
            Require(closed!=null && marked!=null,"Installed entity lifetime API changed");
            marked.Invoke(camera,new object[] {true});
            Require(RemoteView.Feed(remote,camera)==null && RemoteView.Owner(remote,camera)==remote,"Deleted camera retained a feed or removed remote control");
            closed.Invoke(remote,new object[] {true});
            Require(RemoteView.Owner(remote,alternate)==null && !RemoteView.Live(remote),"Deleted remote retained flight input");
            Require(RemoteView.Owner(home,null)==null,"Returning to the home cockpit retained remote context");
            log("PASS remote ownership: home seat/keypad, native/custom turret, camera transfer, no camera, camera power/deletion, remote deletion and A-exit policy");

            var type=typeof(RemoteView);
            var window=(MenuWindow)AccessTools.Field(type,"window").GetValue(null);
            Matrix oldPose=window.Pose; float oldWidth=window.Width,oldAspect=window.Aspect;
            object oldOwner=AccessTools.Field(type,"owner").GetValue(null),oldSource=AccessTools.Field(type,"source").GetValue(null);
            var current=AccessTools.Field(type,"<Current>k__BackingField"); object oldCurrent=current.GetValue(null);
            try
            {
                window.Place(Matrix.CreateRotationY(.27f)*Matrix.CreateTranslation(.3f,1.6f,-.2f));
                window.Width=1.4f; window.Aspect=9f/16;
                Matrix initial=window.Pose;
                Matrix hand=Matrix.CreateTranslation(.2f,1.2f,-.4f);
                window.Begin(1,hand,Vector3.Zero);
                hand.Translation+=new Vector3(.13f,.07f,-.1f); window.Move(hand,Vector3.Zero); window.Stop();
                Require(window.Pose!=initial,"Window movement fixture did not move the screen");
                var resize=new Vector3(window.Width/2,-window.Height/2,0);
                window.Begin(2,hand,resize); window.Move(hand,resize+new Vector3(.2f,-.1f,0)); window.Stop();
                Matrix placed=window.Pose; float width=window.Width;
                var origin=Matrix.CreateRotationY(-.6f)*Matrix.CreateTranslation(-.3f,1.5f,.2f);
                Matrix physicalHead=Matrix.CreateTranslation(.03f,1.6f,.02f);
                var projection=VrMath.Projection(-1,1,-1,1,.03);
                var expected=PhysicalSurface.Quad(null,placed,new RectangleF(-width/2,window.Height/2,width,window.Height),new Vector4(0,0,1,1),Vector4.One,MatrixD.Invert(physicalHead),projection);
                AccessTools.Field(type,"owner").SetValue(null,home);
                foreach(object feed in new object[] {alternate,null,turret,alternate})
                foreach(double scale in new[] {1d,3d,80d})
                foreach(double distance in new[] {0d,1000000000d})
                foreach(double pan in new[] {0d,125d})
                {
                    var ship=MatrixD.CreateFromYawPitchRoll(.8,.3,-.4)*MatrixD.CreateTranslation(distance+pan,distance*.2,-distance);
                    var rig=new CameraRig.Frame(ship,Matrix.Invert(origin),17,scale,scale!=1);
                    var worldHand=(MatrixD)hand*rig.TrackingToWorld;
                    Near(worldHand*MatrixD.Invert(rig.TrackingToWorld),hand,"Virtual ship transforms lost tracking-space hand precision");
                    AccessTools.Field(type,"source").SetValue(null,feed);
                    AccessTools.Method(type,"Publish").Invoke(null,new object[] {0});
                    var view=RemoteView.Current;
                    if(feed==null) { Require(view==null && RemoteView.Active,"Feed loss discarded remote context"); continue; }
                    Near(view.Pose,placed,"Feed or first/third-person transition moved screen");
                    Require(view.Width==width,"View transition resized the screen");
                    var actual=PhysicalSurface.Quad(null,view.Pose,new RectangleF(-width/2,view.Height/2,width,view.Height),new Vector4(0,0,1,1),Vector4.One,MatrixD.Invert(physicalHead),projection);
                    Require(actual.TopLeft==expected.TopLeft && actual.BottomRight==expected.BottomRight,"Virtual pan, rotation, zoom or moving ship changed projected screen");
                    MatrixD tip=MatrixD.CreateTranslation(.04,-.03,.15)*view.Pose;
                    Require(window.Pointer((Matrix)tip,out var hit) && Vector3.Distance(hit,new Vector3(.04f,-.03f,0))<.00001,"Window hit-test frame differs from the display");
                }
                var leaned=physicalHead; leaned.Translation+=new Vector3(.2f,.1f,0);
                var parallax=PhysicalSurface.Quad(null,placed,new RectangleF(-width/2,window.Height/2,width,window.Height),new Vector4(0,0,1,1),Vector4.One,MatrixD.Invert(leaned),projection);
                Require(parallax.TopLeft!=expected.TopLeft,"Window followed physical head movement");
                var press=new InteractionPress();
                press.Update(true,true,false,false,true,true); Require(press.Update(true,true,false,true,true,true),"Near trigger could not grab screen");
                press.Block(); Require(!press.Update(true,true,false,true,true,true) && !press.Held,"Held UI trigger crossed view transition");
                var thrust=new Analog(1); var neutral=new InputAnalogActionData_t {bActive=true,activeOrigin=1};
                thrust.AcceptSample(neutral); var held=neutral; held.y=1; thrust.AcceptSample(held); thrust.BlockUntilRelease();
                thrust.AcceptSample(held); Require(thrust.Position==Vector2.Zero,"Held thrust crossed owner/view transition");
                thrust.AcceptSample(neutral); thrust.AcceptSample(held); Require(thrust.Position.Y==1,"Flight failed to rearm after release");
            }
            finally
            {
                window.Stop(); window.Pose=oldPose; window.Width=oldWidth; window.Aspect=oldAspect;
                AccessTools.Field(type,"owner").SetValue(null,oldOwner); AccessTools.Field(type,"source").SetValue(null,oldSource); current.SetValue(null,oldCurrent);
            }
            log("PASS retained window: explicit move/resize, feed loss/replacement, virtual pan/rotation/scale, billion-metre moving origin, physical head/hand motion and release gates");
        }
    }
}
