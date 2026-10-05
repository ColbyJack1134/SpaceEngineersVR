using System;
using System.IO;
using System.Xml.Serialization;
using SpaceEngineersVR.Config;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Player.Control;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class StickPlacementTests
    {
        private static void Require(bool value,string message) { if (!value) throw new Exception(message); }
        private static void Near(Vector3 a,Vector3 b,string message) => Require(Vector3.Distance(a,b)<.0002f,message);
        public static void Run(Action<string> log)
        {
            var placement=new StickPlacement(); var offset=new Vector3(.12f,.15f,.24f);
            placement.Load(Vector3.Zero,Vector3.Zero);
            placement.Move(true,Vector3.Zero,Vector3.Zero,offset);
            Near(placement.Left,Vector3.Zero,"Locked stick moved");
            placement.Unlock(); placement.Move(true,Vector3.Zero,Vector3.Zero,offset);
            Near(placement.Left,offset,"Unlocked translation failed");
            Near(placement.Right,Vector3.Zero,"Left adjustment moved right stick");
            placement.Lock(); placement.Unlock(); placement.Move(true,offset,Vector3.Zero,-offset); placement.Cancel();
            Require(!placement.Unlocked,"Interrupted adjustment stayed unlocked"); Near(placement.Left,offset,"Interruption failed to restore saved position");
            placement.Load(new Vector3(float.NaN),new Vector3(float.PositiveInfinity));
            Near(placement.Left,Vector3.Zero,"Invalid saved position accepted"); Near(placement.Right,Vector3.Zero,"Invalid right position accepted");
            Near(StickPlacement.Limit(new Vector3(999),true),new Vector3(.40f,.30f,.35f),"Left placement upper bounds");
            Near(StickPlacement.Limit(new Vector3(-999),false),new Vector3(-.40f,-.15f,-.25f),"Right placement lower bounds");
            foreach(bool left in new[] {false,true})
            {
                var contact=left ? CockpitRig.Find(CockpitLayout.Fighter).Left.Contact : CockpitRig.Find(CockpitLayout.Fighter).Right.Contact;
                var center=new Vector3(-contact.X,.03f,-.04f);
                Near(StickPlacement.Limit(center,left),center,"Joystick cannot reach center console");
                var parked=new Vector3(left ? -.18f : .18f,.03f,0);
                Near(StickPlacement.Limit(parked,left),parked,"Joystick cannot be parked off to the side");
            }
            for(int i=0;i<300;i++)
            foreach(bool left in new[] {false,true})
            {
                Vector3 shift=StickPlacement.Limit(new Vector3((float)Math.Sin(i)*.2f,(float)Math.Cos(i)*.2f,(float)Math.Sin(i*.3f)*.3f),left);
                var pivot=left ? CockpitRig.Find(CockpitLayout.Fighter).Left.Pivot : CockpitRig.Find(CockpitLayout.Fighter).Right.Pivot;
                var contact=left ? CockpitRig.Find(CockpitLayout.Fighter).Left.Contact : CockpitRig.Find(CockpitLayout.Fighter).Right.Contact;
                var axes=new Vector3(.4f,-.3f,.6f);
                Matrix articulation=left ? CockpitRig.Find(CockpitLayout.Fighter).Left.Visual(new Vector3(-axes.Z,axes.Y,axes.X)) : CockpitRig.Find(CockpitLayout.Fighter).Right.Visual(axes);
                Matrix visual=StickPlacement.Visual(articulation,shift);
                Near(Vector3.Transform(pivot,visual),pivot+shift,"Adjusted articulated pivot separated from base");
                Near(Vector3.Transform(contact,visual),Vector3.Transform(contact,articulation)+shift,"Grab target separated from moved mesh");
                Matrix grip=Matrix.CreateFromYawPitchRoll(.3f,.1f,-.2f);
                Matrix attached=(left ? CockpitRig.Find(CockpitLayout.Fighter).Left:CockpitRig.Find(CockpitLayout.Fighter).Right).Palm(left);
                Matrix wrist=attached*visual;
                Near(Vector3.Transform(new Vector3(-.105f,-.035f,0),wrist),Vector3.Transform(contact+attached.Backward*(left ? -.035f:.035f)+attached.Up*.015f,visual),"Raised palm left moved handle");
                MatrixD ship=MatrixD.CreateFromYawPitchRoll(i*.02,i*.03,i*.01); ship.Translation=new Vector3D(2e6+i*40,-3e6,4e6);
                Near((Vector3)Vector3D.Transform(Vector3D.Transform(contact,(MatrixD)visual*ship),MatrixD.Invert(ship)),Vector3.Transform(contact,visual),"Moving ship corrupted placement");
            }
            var gripGate=new GripCapture(); gripGate.Update(true,false,true,true); gripGate.Update(true,true,true,true); gripGate.Release();
            Require(!gripGate.Update(true,true,true,true) && !gripGate.Held && gripGate.Consumed,"Lock/interrupt turned adjustment grab into flight");
            gripGate.Update(true,false,true,true); Require(gripGate.Update(true,true,true,true),"Lock prevented fresh flight grab");
            var config=new PluginConfig { JetpackRoll=.33f,ShipRollSensitivity=.77f,
                SeatFits=new[] { new SeatFitSetting { Subtype=CockpitLayout.Fighter,Y=.1f } },
                StickPlacements=new[] { new StickPlacementSetting { Subtype=CockpitLayout.Fighter,LeftX=.12f,RightZ=.24f } } };
            var serializer=new XmlSerializer(typeof(PluginConfig));
            using(var writer=new StringWriter())
            {
                serializer.Serialize(writer,config);
                var restored=(PluginConfig)serializer.Deserialize(new StringReader(writer.ToString()));
                Require(restored.StickPlacements[0].LeftX==.12f && restored.StickPlacements[0].RightZ==.24f && restored.SeatFits[0].Y==.1f &&
                    restored.JetpackRoll==.33f && restored.ShipRollSensitivity==.77f,"Placement persistence corrupts seat/roll settings");
            }
            log("PASS repositionable sticks: explicit unlock/lock/cancel, independent offsets and bounds, invalid config, 600 moved pivot/contact/palm and large-coordinate ship checks, held-grip release gate and config round trip preserving seat/roll.");
        }
    }
}
