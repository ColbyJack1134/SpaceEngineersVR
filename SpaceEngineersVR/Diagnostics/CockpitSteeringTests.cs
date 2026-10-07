using System;
using System.IO;
using HarmonyLib;
using VRageRender;
using VRageRender.Import;
using SpaceEngineersVR.Player;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class CockpitSteeringTests
    {
        internal static void Run(Action<string> log)
        {
            foreach(string subtype in new[] {"RoverCockpit","BuggyCockpit","SpeederCockpit","SpeederCockpitCompact"})
            {
                var wheel=CockpitRig.Find(subtype).Wheel;
                var state=new CockpitSteering();
                var l=wheel.Contact(true,0); var r=wheel.Contact(false,0);
                Near(state.Update(wheel,true,l,false,r,.016f),0,"capture");
                l=wheel.Contact(true,.6f);
                Near(state.Update(wheel,true,l,false,r,.016f),.6f,"left-hand steering");
                r=wheel.Contact(false,.6f);
                Near(state.Update(wheel,true,l,true,r,.016f),.6f,"second-hand join");
                l=wheel.Contact(true,.8f); r=wheel.Contact(false,.8f);
                Near(state.Update(wheel,true,l,true,r,.016f),.8f,"two-hand steering");
                Near(state.Update(wheel,false,l,true,r,.016f),.8f,"handoff");
                r=wheel.Contact(false,1);
                Near(state.Update(wheel,false,l,true,r,.016f),1,"right-hand steering");
                Near(state.Update(wheel,false,l,true,r+wheel.Axis*.3f,.016f),1,"axis translation");
                Near(state.Update(wheel,false,l,false,r,.016f),0,"release command");
                if(state.Position<=0 || state.Position>=1) throw new Exception("Steering does not return smoothly: "+subtype);
                state.Reset(); Near(state.Position,0,"reset");
                state.Update(wheel,true,wheel.Contact(true,0),false,r,.016f);
                Near(state.Update(wheel,true,new Vector3(float.NaN,0,0),false,r,.016f),0,"invalid tracking");
            }
            var buggy=CockpitRig.Find("BuggyCockpit").Wheel;
            foreach(bool left in new[] {true,false})
            {
                var palm=buggy.Palm(left,0);
                if(palm.Right.Y>=-.25f || Vector3.Distance(Vector3.Transform(TrackedArms.BarFingerCavity,palm),buggy.Contact(left,0))>.0001f)
                    throw new Exception("Buggy wrist pitch loses the rim contact or points above the wheel");
            }
            foreach(string subtype in new[] {"SpeederCockpit","SpeederCockpitCompact"})
            {
                var wheel=CockpitRig.Find(subtype).Wheel;
                var grip=wheel.Palm(false,0);
                var turn=new WristKnob.Turn(wheel.RightShaft,wheel.ThrottleRange,1);
                turn.Begin(grip,0);
                foreach(float steering in new[] {-.8f,.3f,1f})
                {
                    Matrix tracked=grip*wheel.Visual(steering);
                    turn.Move(tracked*Matrix.Invert(wheel.Visual(steering)));
                    Near(turn.Value,0,"steering must not open throttle");
                }
                turn.Move(grip*Matrix.CreateFromAxisAngle(wheel.RightShaft,wheel.ThrottleRange*.5f));
                Near(turn.Value,.5f,"half throttle");
                turn.Move(grip*Matrix.CreateFromAxisAngle(wheel.RightShaft,wheel.ThrottleRange*1.5f));
                Near(turn.Value,1,"throttle stop");
                turn.Move(grip*Matrix.CreateFromAxisAngle(wheel.RightShaft,wheel.ThrottleRange));
                Near(turn.Value,.5f,"throttle reversal");
                turn.Move(grip*Matrix.CreateFromAxisAngle(wheel.RightShaft,-wheel.ThrottleRange));
                Near(turn.Value,0,"closed throttle stop");
                if(wheel.Palm(true,.6f,0)!=wheel.Palm(true,.6f,1)) throw new Exception("Throttle moves left grip");
                if(Vector3.Distance(wheel.Palm(false,0,1).Translation,grip.Translation)<.001f)
                    throw new Exception("Throttle does not carry the attached palm");
            }
            if(!CockpitStickMath.GripAligned(Matrix.Identity,Matrix.CreateRotationX(MathHelper.ToRadians(60))) ||
                CockpitStickMath.GripAligned(Matrix.Identity,Matrix.CreateRotationX(MathHelper.Pi)))
                throw new Exception("Steering grip permits an upside-down wrist or rejects normal reach");
            if(HarmonyLib.AccessTools.Field(typeof(Sandbox.Game.Entities.Blocks.MyTextPanelComponent),"m_previousTextureID")?.FieldType!=typeof(string))
                throw new Exception("Native cockpit LCD texture source changed");
            ScreenVisibility();
            log("PASS live moving LCD hide flags survive visibility refresh and release");
            log("PASS motorcycle throttle steering isolation, stops, reversal, right-only palm motion and steering grip orientation");
            log("PASS physical steering one-hand motion, two-hand join, handoff, axis rejection, release and tracking loss");
        }
        private static void ScreenVisibility()
        {
            var active=AccessTools.Field(typeof(CockpitRender),"activeRig");
            var check=AccessTools.Field(typeof(CockpitRender),"verification");
            object previousRig=active.GetValue(null),previousCheck=check.GetValue(null);
            var rig=CockpitRig.Find("RoverCockpit");
            string content=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(MyModelImporter).Assembly.Location),"..","Content"));
            try
            {
                active.SetValue(null,rig);
                check.SetValue(null,new CockpitRender.Verification(123,rig.Geometry(content),new string[0]));
                RenderFlags add=RenderFlags.Visible,remove=CockpitRender.Hidden;
                CockpitRender.PreserveScreenVisibility(123,"CockpitScreen_04",ref add,ref remove);
                if((add&CockpitRender.Hidden)!=CockpitRender.Hidden || (remove&CockpitRender.Hidden)!=0)
                    throw new Exception("Live LCD update exposes the original moving screen");
                var converter=AccessTools.Method(AccessTools.TypeByName("VRageRender.MyProxiesFactory"),"GetRenderableProxyFlags");
                object added=converter.Invoke(null,new object[] {add}),removed=converter.Invoke(null,new object[] {remove});
                long hidden=Convert.ToInt64(Enum.Parse(added.GetType(),"SkipInMainView, SkipInDepth, SkipInForward"));
                if((Convert.ToInt64(added)&~Convert.ToInt64(removed)&hidden)!=hidden)
                    throw new Exception("Native flag conversion clears moving screen suppression");
                foreach(var item in new[] {Tuple.Create(124u,"CockpitScreen_04"),Tuple.Create(123u,"CockpitScreen_01"),Tuple.Create(123u,(string)null)})
                {
                    add=RenderFlags.Visible; remove=0;
                    CockpitRender.PreserveScreenVisibility(item.Item1,item.Item2,ref add,ref remove);
                    if(add!=RenderFlags.Visible || remove!=0) throw new Exception("LCD suppression affects another actor or fixed screen");
                }
                check.SetValue(null,null);
                add=RenderFlags.Visible; remove=RenderFlags.Visible|CockpitRender.Hidden;
                CockpitRender.PreserveScreenVisibility(123,"CockpitScreen_04",ref add,ref remove);
                if(add!=RenderFlags.Visible || remove!=(RenderFlags.Visible|CockpitRender.Hidden))
                    throw new Exception("Released LCD cannot restore native visibility");
            }
            finally { active.SetValue(null,previousRig); check.SetValue(null,previousCheck); }
        }
        private static void Near(float value,float expected,string name)
        {
            if(!float.IsNaN(value) && Math.Abs(value-expected)<.0001f) return;
            throw new Exception("Steering "+name+": "+value+" != "+expected);
        }
    }
}
