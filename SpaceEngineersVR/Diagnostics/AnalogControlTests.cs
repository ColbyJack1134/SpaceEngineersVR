using System;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using Sandbox.ModAPI.Ingame;
using SpaceEngineersVR.Multiplayer;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class AnalogControlTests
    {
        private sealed class Actuator : RealProxy
        {
            internal float Min,Max,Position,Velocity,Override;
            internal Sandbox.ModAPI.Interfaces.ITerminalProperty<float> Property;
            internal Actuator(Type type) : base(type) { }
            public override IMessage Invoke(IMessage message)
            {
                var call=(IMethodCallMessage)message; object result=null;
                switch(call.MethodName)
                {
                    case "GetProperty": result=Property; break;
                    case "get_MinLimit": case "get_LowerLimitRad": result=Min; break;
                    case "get_MaxLimit": case "get_UpperLimitRad": result=Max; break;
                    case "get_CurrentPosition": case "get_Angle": result=Position; break;
                    case "get_Velocity": case "get_TargetVelocityRad": result=Velocity; break;
                    case "set_Velocity": case "set_TargetVelocityRad": Velocity=(float)call.Args[0]; break;
                    case "get_ThrustOverridePercentage": result=Override; break;
                    case "set_ThrustOverridePercentage": Override=(float)call.Args[0]; break;
                    default: throw new Exception("Unexpected actuator call: "+call.MethodName);
                }
                return new ReturnMessage(result,null,0,call.LogicalCallContext,call);
            }
        }
        private static void Require(bool value,string reason) { if(!value) throw new Exception(reason); }
        internal static void Run(Action<string> log)
        {
            foreach(bool rotor in new[] {false,true})
            {
                var state=new Actuator(rotor ? typeof(IMyMotorStator):typeof(IMyPistonBase)) {Min=rotor ? -.8f:2,Max=rotor ? .8f:8,Position=rotor ? 0:2};
                var block=(IMyTerminalBlock)state.GetTransparentProxy();
                var channel=AnalogControl.Resolve(block,rotor ? "RotateToAngle":"SetAndMove",rotor ? 10:2);
                Require(channel!=null && Math.Abs(channel.Value(.5f)-(rotor ? 0:5))<.0001f,"Lever midpoint ignored configured limits");
                Require(channel.Value(AnalogControl.EndZone*.5f)==state.Min && channel.Value(1-AnalogControl.EndZone*.5f)==state.Max,"Lever end zone missed the exact limit");
                Require(channel.Value(AnalogControl.EndZone+.01f)>state.Min,"Lever end zone extends past its detent");
                foreach(float observed in new[] {0f,.3f,.6f,1f})
                {
                    state.Position=state.Min+(state.Max-state.Min)*observed;
                    float lever=channel.Position();
                    Require(Math.Abs(channel.Value(lever)-state.Position)<.0001f && (observed%1!=0 || lever==observed),"External actuator movement did not update lever feedback");
                }
                foreach(float position in new[] {.75f,.25f,1f,0f})
                {
                    float target=channel.Value(position);
                    for(int tick=0;tick<1800;tick++)
                    {
                        if(!AnalogControl.Drive(channel,target)) break;
                        state.Position+=state.Velocity/60;
                        Require(state.Position>=state.Min-.001f && state.Position<=state.Max+.001f,"Servo crossed configured limits");
                    }
                    Require(Math.Abs(state.Position-target)<.003f && state.Velocity==0,"Actuator did not reach and stop at lever target");
                }
                state.Max=rotor ? .4f:4;
                for(int tick=0;tick<1800;tick++)
                {
                    if(!AnalogControl.Drive(channel,100)) break;
                    state.Position+=state.Velocity/60;
                }
                Require(Math.Abs(state.Position-state.Max)<.003f,"Changed terminal limit did not clamp an existing target");
                Require(!AnalogControl.Drive(channel,float.NaN) && state.Velocity==0,"Invalid target kept actuator moving");
                channel.Speed=0;
                Require(!AnalogControl.Drive(channel,state.Min),"Zero assigned speed was replaced with an invented speed");
                Require(channel.Label().Contains(rotor ? "°":"m"),"Actuator value label lost its unit");
            }
            Require(Math.Abs(AnalogControl.AngleInRange(MathHelper.TwoPi-.4f,-.8f,.8f)+.4f)<.0001f,"Wrapped rotor angle escaped configured range");
            var unlimited=new Actuator(typeof(IMyMotorStator)) {Min=float.MinValue,Max=float.MaxValue,Position=6};
            var freeRotor=AnalogControl.Resolve((IMyTerminalBlock)unlimited.GetTransparentProxy(),"RotateToAngle",10);
            foreach(float target in new[] {.1f,MathHelper.TwoPi})
            {
                for(int tick=0;tick<1800;tick++)
                {
                    if(!AnalogControl.Drive(freeRotor,target)) break;
                    unlimited.Position=(unlimited.Position+unlimited.Velocity/60+MathHelper.TwoPi)%MathHelper.TwoPi;
                }
                Require(Math.Abs(MathHelper.WrapAngle(unlimited.Position-target))<.002f && unlimited.Velocity==0,"Unlimited rotor failed to stop across its angle wrap");
            }
            var thruster=new Actuator(typeof(IMyThrust)) {Override=.4f};
            var thrust=new System.Collections.Generic.List<AnalogControl.Channel> {AnalogControl.Resolve((IMyTerminalBlock)thruster.GetTransparentProxy(),"SetOverride")};
            AnalogControl.Set(null,0,thrust,.5f);
            Require(Math.Abs(thruster.Override-.5f)<.0001f,"Thrust override handle missed its midpoint");
            AnalogControl.Set(null,0,thrust,AnalogControl.EndZone*.5f);
            Require(thruster.Override==0 && !thrust[0].Label().Contains("%"),"Thrust override handle near its rear stop left the override enabled");
            log("PASS analog actuators: piston/rotor range mapping, end zones reaching exact limits, thrust override disabled at the rear stop, external movement feedback, intermediate targets, reversal, stop, changing limits, wrapped angles, zero speed and invalid targets; limit setters never called.");
        }
        internal static void RunNative(Action<string> log)
        {
            float scalar=10;
            var slider=new Sandbox.Game.Gui.MyTerminalControlSlider<Sandbox.Game.Entities.Blocks.MyPistonBase>("Range",VRage.Utils.MyStringId.NullOrEmpty,VRage.Utils.MyStringId.NullOrEmpty);
            slider.SetLogLimits(1,1000); slider.Getter=_=>scalar; slider.Setter=(_,value)=>scalar=value;
            var sliderOwner=new Actuator(typeof(IMyPistonBase)) {Property=slider};
            var numeric=AnalogControl.Resolve((IMyTerminalBlock)sliderOwner.GetTransparentProxy(),"IncreaseRange");
            Require(numeric!=null,"Native increase action did not resolve its slider");
            numeric.Block=(IMyTerminalBlock)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(Sandbox.Game.Entities.Blocks.MyPistonBase));
            float lever=AnalogControl.ToLever(2f/3);
            Require(Math.Abs(numeric.Position()-AnalogControl.ToLever(1f/3))<.0001f && Math.Abs(numeric.Value(lever)-100)<.01f,"Native logarithmic slider mapping was replaced with linear interpolation");
            numeric.Property.SetValue(numeric.Block,numeric.Value(lever));
            Require(Math.Abs(scalar-100)<.01f,"Native slider setter did not receive the lever value");
            Require(AnalogControl.Resolve((IMyTerminalBlock)sliderOwner.GetTransparentProxy(),"OnOff")==null,"Boolean switch became analog");
            log("PASS native analog slider: logarithmic range mapping, setter and nonnumeric fallback.");
        }
    }
}
