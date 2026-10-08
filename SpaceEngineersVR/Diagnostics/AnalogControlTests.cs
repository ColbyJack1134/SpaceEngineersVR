using System;
using System.Collections.Generic;
using Sandbox.Game.Screens.Helpers;
using Sandbox.ModAPI.Interfaces;
using VRage.Collections;
using VRage.Game;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using Sandbox.ModAPI.Ingame;
using SpaceEngineersVR.Multiplayer;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class AnalogControlTests
    {
        private sealed class ParameterItem : MyToolbarItemTerminalBlock, IUserCustomizableTerminalAction
        {
            internal Sandbox.ModAPI.IMyTerminalBlock Owner;
            internal readonly List<Sandbox.Game.Gui.ITerminalAction> Actions=new List<Sandbox.Game.Gui.ITerminalAction>();
            public override ListReader<Sandbox.Game.Gui.ITerminalAction> AllActions => Actions;
            IMyTerminalBlock IUserCustomizableTerminalAction.GetBlock() => Owner;
        }
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
                    case "GetProperty": result=Property?.Id==(string)call.Args[0] ? Property:null; break;
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
            var piston=(Sandbox.Game.Entities.Blocks.MyPistonBase)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(Sandbox.Game.Entities.Blocks.MyPistonBase));
            piston.SlimBlock=(Sandbox.Game.Entities.Cube.MySlimBlock)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(Sandbox.Game.Entities.Cube.MySlimBlock));
            piston.SlimBlock.BlockDefinition=new Sandbox.Definitions.MyPistonBaseDefinition {MaxVelocity=2};
            HarmonyLib.AccessTools.Field(piston.GetType(),"m_currentPos").SetValue(piston,3.25f);
            var move=new Sandbox.Game.Gui.MyTerminalAction<Sandbox.Game.Entities.Blocks.MyPistonBase>("SetAndMove",new System.Text.StringBuilder("Move"),"");
            move.ParameterDefinitions.Add(TerminalActionParameter.Get(""));
            move.ParameterDefinitions.Add(TerminalActionParameter.Get(""));
            var fresh=new ParameterItem {Owner=piston,ActionId="SetAndMove"}; fresh.Actions.Add(move);
            AnalogControl.DefaultParameters(fresh,fresh.ActionId);
            Require(fresh.Parameters.Count==2 && (float)fresh.Parameters[0].Value==3.25f && (float)fresh.Parameters[1].Value==2,
                "Fresh analog assignment omitted native position/speed defaults");
            var terminal=(MyToolbarItemTerminalBlock)MyToolbarItemFactory.CreateToolbarItem(new Sandbox.Common.ObjectBuilders.MyObjectBuilder_ToolbarItemTerminalBlock {BlockEntityId=987654322,_Action="SetAndMove"});
            foreach(var parameter in fresh.Parameters) terminal.Parameters.Add(parameter);
            var copied=(MyToolbarItemTerminalBlock)HarmonyLib.AccessTools.Method(typeof(Player.CockpitAssignmentToolbar),"Copy").Invoke(null,new object[] {terminal});
            Require(copied.Parameters.Count==2 && (float)copied.Parameters[1].Value==2,"Assignment copy lost initialized movement parameters");
            terminal.Parameters[1]=TerminalActionParameter.Get(0f);
            Require((float)copied.Parameters[1].Value==2,"Assignment copy shared mutable movement parameters");
            var rotor=(Sandbox.Game.Entities.Cube.MyMotorStator)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(Sandbox.Game.Entities.Cube.MyMotorStator));
            rotor.SlimBlock=(Sandbox.Game.Entities.Cube.MySlimBlock)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(Sandbox.Game.Entities.Cube.MySlimBlock));
            rotor.SlimBlock.CubeGrid=(Sandbox.Game.Entities.MyCubeGrid)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(Sandbox.Game.Entities.MyCubeGrid));
            HarmonyLib.AccessTools.Field(rotor.GetType(),"m_currentAngle").SetValue(rotor,MathHelper.PiOver2);
            var rotate=new Sandbox.Game.Gui.MyTerminalAction<Sandbox.Game.Entities.Cube.MyMotorStator>("RotateToAngle",new System.Text.StringBuilder("Rotate"),"");
            for(int i=0;i<3;i++) rotate.ParameterDefinitions.Add(TerminalActionParameter.Get(""));
            var freshRotor=new ParameterItem {Owner=rotor,ActionId="RotateToAngle"}; freshRotor.Actions.Add(rotate);
            var environment=Sandbox.Game.World.MySector.EnvironmentDefinition;
            try
            {
                Sandbox.Game.World.MySector.EnvironmentDefinition=new Sandbox.Definitions.MyEnvironmentDefinition();
                HarmonyLib.AccessTools.Field(typeof(Sandbox.Definitions.MyEnvironmentDefinition),"m_largeShipMaxAngularSpeedInRadians").SetValue(Sandbox.Game.World.MySector.EnvironmentDefinition,MathHelper.Pi);
                AnalogControl.DefaultParameters(freshRotor,freshRotor.ActionId);
            }
            finally { Sandbox.Game.World.MySector.EnvironmentDefinition=environment; }
            Require(freshRotor.Parameters.Count==3 && Math.Abs((float)freshRotor.Parameters[0].Value-90)<.001f &&
                (float)freshRotor.Parameters[1].Value>0 && (long)freshRotor.Parameters[2].Value==(long)MyRotationDirection.AUTO,
                "Fresh rotor assignment omitted angle, speed or automatic direction");
            log("PASS native fresh analog assignment: empty parameter lists initialized from native definitions, actual piston position/rotor angle/full speed, automatic direction and independent serialized parameter copy.");
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
            float ratio=50; bool targeted=false;
            var distance=new Sandbox.Game.Gui.MyTerminalControlSlider<Sandbox.Game.Entities.MyJumpDrive>("JumpDistance",VRage.Utils.MyStringId.NullOrEmpty,VRage.Utils.MyStringId.NullOrEmpty);
            distance.SetLimits(0,100); distance.Getter=_=>ratio; distance.Setter=(_,value)=>ratio=value; distance.Enabled=_=>!targeted;
            var jump=AnalogControl.Resolve((IMyTerminalBlock)new Actuator(typeof(Sandbox.ModAPI.IMyJumpDrive)) {Property=distance}.GetTransparentProxy(),"SetGravityAcceleration");
            Require(jump!=null,"Native jump distance Set action did not resolve its slider");
            jump.Block=(IMyTerminalBlock)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(Sandbox.Game.Entities.MyJumpDrive));
            var jumps=new System.Collections.Generic.List<AnalogControl.Channel> {jump};
            AnalogControl.Set(null,0,jumps,1);
            Require(ratio==100,"Jump distance handle did not reach the full distance");
            targeted=true; AnalogControl.Set(null,0,jumps,0);
            Require(ratio==100,"Jump distance changed while the native slider was disabled by a selected target");
            log("PASS native analog slider: logarithmic range mapping, setter, mismatched Set action IDs, disabled sliders and nonnumeric fallback.");
        }
    }
}
