using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Sandbox.Game.Entities;
using Sandbox.Game.Screens.Helpers;
using Sandbox.ModAPI.Interfaces;
using Sandbox.ModAPI.Interfaces.Terminal;
using Sandbox.ModAPI.Ingame;
using VRageMath;
using Block=Sandbox.ModAPI.Ingame.IMyTerminalBlock;

namespace SpaceEngineersVR.Multiplayer
{
    internal static class AnalogControl
    {
        internal static bool IsHandle(string subtype,int slot)
        {
            switch(subtype)
            {
                case "OpenCockpitLarge": return slot>=59 && slot<61;
                case "LargeBlockModularBridgeCockpit": return slot>=0 && slot<1;
                case "RoverCockpit": return slot>=0 && slot<1;
                case "SmallBlockFlushCockpit": return slot>=0 && slot<1;
                case "LargeBlockSuspendedControlSeat": return slot>=0 && slot<1;
                case "LargeBlockSuspendedControlSeatB": return slot>=0 && slot<1;
                case "SmallBlockSuspendedControlSeat": return slot>=0 && slot<1;
                case "SmallBlockSuspendedControlSeatB": return slot>=0 && slot<1;
                case "SmallBlockCapCockpit": return slot>=14 && slot<15;
                case "SpeederCockpit": return slot>=0 && slot<1;
                case "SpeederCockpitCompact": return slot>=0 && slot<1;
                case "LargeBlockOpenSlopedCockpit": return slot>=14 && slot<17;
                case "LargeBlockClosedSlopedCockpit": return slot>=14 && slot<17;
                case "SmallBlockOpenSlopedCockpit": return slot>=0 && slot<3;
                case "SmallBlockClosedSlopedCockpit": return slot>=0 && slot<3;
                case "SmallBlockStandingCockpit": return slot>=6 && slot<7;
                case "LargeBlockStandingCockpit": return slot>=6 && slot<7;
                default: return false;
            }
        }
        internal enum Kind { Slider, Piston, Rotor, Thrust }
        // The last 2% of travel at each end holds the exact limit, so the rear stop reaches 0 (thrust override disabled).
        internal const float EndZone=.02f;
        internal static float FromLever(float position) => MathHelper.Clamp((position-EndZone)/(1-2*EndZone),0,1);
        internal static float ToLever(float fraction) => fraction<=0 ? 0 : fraction>=1 ? 1 : EndZone+fraction*(1-2*EndZone);
        internal sealed class Channel
        {
            internal Block Block;
            internal Kind Type;
            internal ITerminalProperty<float> Property;
            internal Delegate Normalize,Denormalize;
            internal float Speed;
            internal bool Range(out float min,out float max)
            {
                min=0; max=1;
                if(Type==Kind.Piston) { var p=(IMyPistonBase)Block; min=p.MinLimit; max=p.MaxLimit; }
                else if(Type==Kind.Rotor)
                {
                    var r=(IMyMotorStator)Block;
                    min=r.LowerLimitRad; max=r.UpperLimitRad;
                    if(!Finite(min) || min < -MathHelper.TwoPi) min=Finite(max) && max<=0 ? -MathHelper.TwoPi:0;
                    if(!Finite(max) || max > MathHelper.TwoPi) max=MathHelper.TwoPi;
                }
                else if(Type==Kind.Slider) { min=Property.GetMinimum(Block); max=Property.GetMaximum(Block); }
                return Finite(min) && Finite(max) && max>min;
            }
            internal float Actual => Type==Kind.Piston ? ((IMyPistonBase)Block).CurrentPosition :
                Type==Kind.Rotor ? ((IMyMotorStator)Block).Angle : Type==Kind.Thrust ? ((IMyThrust)Block).ThrustOverridePercentage : Property.GetValue(Block);
            internal float Value(float position)
            {
                if(!Range(out float min,out float max)) return Actual;
                position=FromLever(position);
                return Type==Kind.Slider && Denormalize!=null ? (float)Denormalize.DynamicInvoke(Block,position) : MathHelper.Lerp(min,max,position);
            }
            internal float Position()
            {
                if(!Range(out float min,out float max)) return 0;
                float actual=Actual;
                if(Type==Kind.Rotor) actual=AngleInRange(actual,min,max);
                return ToLever(MathHelper.Clamp(Type==Kind.Slider && Normalize!=null ? (float)Normalize.DynamicInvoke(Block,actual) : (actual-min)/(max-min),0,1));
            }
            internal string Label(float? position=null)
            {
                float value=position.HasValue ? Value(position.Value):Actual;
                if(Type==Kind.Piston) return value.ToString("0.00",CultureInfo.CurrentCulture)+" m";
                if(Type==Kind.Rotor)
                {
                    if(Range(out float min,out float max)) value=AngleInRange(value,min,max);
                    return MathHelper.ToDegrees(value).ToString("0.0",CultureInfo.CurrentCulture)+"°";
                }
                if(Type==Kind.Thrust) return value<=0 ? VRage.MyTexts.GetString(MyCommonTexts.Disabled) : (value*100).ToString("0.0",CultureInfo.CurrentCulture)+"%";
                var writer=(Property as IMyTerminalControlSlider)?.Writer;
                if(writer!=null && !position.HasValue) { var text=new StringBuilder(); writer((Sandbox.ModAPI.IMyTerminalBlock)Block,text); return text.ToString(); }
                return value.ToString("0.##",CultureInfo.CurrentCulture);
            }
        }
        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        internal static float AngleInRange(float angle,float min,float max) => angle+(float)Math.Round(((min+max)*.5f-angle)/MathHelper.TwoPi)*MathHelper.TwoPi;
        internal static List<Channel> Resolve(MyToolbarItem item,long identity,bool requireEnabled=true)
        {
            if(!(item is MyToolbarItemActions action) || string.IsNullOrEmpty(action.ActionId)) return null;
            var blocks=new List<Block>();
            if(item is MyToolbarItemTerminalBlock block) block.FetchAllBlocks(blocks);
            else if(item is MyToolbarItemTerminalGroup group) group.FetchAllBlocks(blocks);
            if(blocks.Count==0) return null;
            float speed=0;
            var parameters=(item as IUserCustomizableTerminalAction)?.Parameters;
            if(parameters!=null && parameters.Count>1 && parameters[1].Value is float supplied) speed=Math.Abs(supplied);
            if(!Finite(speed)) return null;
            var result=new List<Channel>();
            foreach(var target in blocks)
            {
                if(!(target is Sandbox.ModAPI.IMyTerminalBlock accessible) || !accessible.HasPlayerAccess(identity)) return null;
                var native=target.GetActionWithName(action.ActionId);
                if(native==null || requireEnabled && !native.IsEnabled(target)) return null;
                var channel=Resolve(target,action.ActionId,speed);
                if(channel==null) return null;
                result.Add(channel);
            }
            return result;
        }
        internal static Channel Resolve(Block block,string action,float speed=0)
        {
            var channel=new Channel { Block=block,Speed=speed };
            if(action=="SetAndMove" && block is IMyPistonBase) channel.Type=Kind.Piston;
            else if(action=="RotateToAngle" && block is IMyMotorStator) channel.Type=Kind.Rotor;
            else if(action=="SetOverride" && block is IMyThrust) channel.Type=Kind.Thrust;
            else
            {
                string id=action.StartsWith("Increase",StringComparison.Ordinal) || action.StartsWith("Decrease",StringComparison.Ordinal) ? action.Substring(8) :
                    action.StartsWith("Set",StringComparison.Ordinal) ? action.Substring(3):null;
                if(id==null || !(block.GetProperty(id) is ITerminalProperty<float> property) || !(property is IMyTerminalControlSlider)) return null;
                channel.Type=Kind.Slider; channel.Property=property;
                var type=property.GetType();
                channel.Normalize=type.GetField("Normalizer")?.GetValue(property) as Delegate;
                channel.Denormalize=type.GetField("Denormalizer")?.GetValue(property) as Delegate;
            }
            return channel;
        }
        private sealed class Motion
        {
            internal Channel Channel;
            internal MyCockpit Seat;
            internal long Pilot,Identity;
            internal float Target,LastVelocity;
        }
        private static readonly Dictionary<long,Motion> motions=new Dictionary<long,Motion>();
        internal static void Set(MyCockpit seat,long identity,List<Channel> channels,float position)
        {
            if(!Finite(position) || position<0 || position>1) return;
            foreach(var channel in channels)
            {
                if(!channel.Range(out _,out _)) continue;
                float value=channel.Value(position);
                if(!Finite(value)) continue;
                if(channel.Type==Kind.Slider)
                {
                    if(channel.Property.Id=="Velocity") motions.Remove(channel.Block.EntityId);
                    channel.Property.SetValue(channel.Block,value);
                }
                else if(channel.Type==Kind.Thrust) ((IMyThrust)channel.Block).ThrustOverridePercentage=value;
                else
                {
                    var motion=new Motion { Channel=channel,Seat=seat,Pilot=seat.Pilot.EntityId,Identity=identity,Target=value };
                    motion.LastVelocity=Velocity(channel);
                    motions[channel.Block.EntityId]=motion;
                    Step(motion);
                }
            }
        }
        private static float Velocity(Channel c) => c.Type==Kind.Piston ? ((IMyPistonBase)c.Block).Velocity:((IMyMotorStator)c.Block).TargetVelocityRad;
        private static void Velocity(Channel c,float value)
        {
            if(c.Type==Kind.Piston) ((IMyPistonBase)c.Block).Velocity=value;
            else ((IMyMotorStator)c.Block).TargetVelocityRad=value;
        }
        internal static float TargetVelocity(float error,float speed,float tolerance) => Math.Abs(error)<=tolerance ? 0 : MathHelper.Clamp(error*4,-speed,speed);
        private static bool Step(Motion motion)
        {
            var c=motion.Channel;
            if(c.Block.Closed) return false;
            // A terminal or another action takes ownership as soon as it changes velocity.
            if(Math.Abs(Velocity(c)-motion.LastVelocity)>.0001f) return false;
            if(motion.Seat.Closed || motion.Seat.Pilot?.EntityId!=motion.Pilot ||
                !((Sandbox.ModAPI.IMyTerminalBlock)c.Block).HasPlayerAccess(motion.Identity) || !c.Range(out _,out _))
            { Velocity(c,0); return false; }
            bool moving=Drive(c,motion.Target);
            motion.LastVelocity=Velocity(c);
            return moving;
        }
        internal static bool Drive(Channel c,float target)
        {
            if(!c.Range(out float min,out float max) || !Finite(target) || !Finite(c.Actual) || !Finite(c.Speed))
            { Velocity(c,0); return false; }
            float actual=c.Type==Kind.Rotor ? AngleInRange(c.Actual,min,max):c.Actual;
            float error=MathHelper.Clamp(target,min,max)-actual;
            if(c.Block is IMyMotorStator rotor && rotor.LowerLimitRad < -MathHelper.TwoPi && rotor.UpperLimitRad > MathHelper.TwoPi) error=MathHelper.WrapAngle(error);
            float speed=c.Type==Kind.Piston ? c.Speed : c.Speed*MathHelper.Pi/30;
            float velocity=TargetVelocity(error,speed,c.Type==Kind.Piston ? .002f:.001f);
            if(Math.Abs(velocity-Velocity(c))>.0001f) Velocity(c,velocity);
            return velocity!=0;
        }
        internal static void Update()
        {
            foreach(var entry in motions.ToArray())
                try { if(!Step(entry.Value)) motions.Remove(entry.Key); }
                catch(Exception error) { motions.Remove(entry.Key); MultiplayerRuntime.Log("Analog control stopped: "+error.Message); }
        }
        internal static void SavedVelocity(long block,VRage.Game.MyObjectBuilder_CubeBlock builder)
        {
            if(!motions.TryGetValue(block,out var motion) || Math.Abs(Velocity(motion.Channel)-motion.LastVelocity)>.0001f) return;
            // A saved or copied actuator must not retain velocity without its transient target controller.
            if(builder is Sandbox.Common.ObjectBuilders.MyObjectBuilder_PistonBase piston) piston.Velocity=0;
            if(builder is Sandbox.Common.ObjectBuilders.MyObjectBuilder_MotorStator rotor) rotor.TargetVelocity=0;
        }
        internal static void Reset()
        {
            foreach(var motion in motions.Values)
                if(!motion.Channel.Block.Closed && Math.Abs(Velocity(motion.Channel)-motion.LastVelocity)<.0001f) Velocity(motion.Channel,0);
            motions.Clear();
        }
    }
}
