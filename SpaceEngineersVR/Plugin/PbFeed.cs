using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Blocks;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.Gui;
using Sandbox.Game.Screens.Terminal.Controls;
using Sandbox.Game.Multiplayer;
using Sandbox.Game.World;
using Sandbox.ModAPI.Ingame;
using SpaceEngineersVR.Multiplayer;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Player.Control;
using VRage.Utils;
using VRageMath;

namespace SpaceEngineersVR.Plugin
{
    // A client's Run becomes a reliable server request (MyProgrammableBlock.SendRunProgramRequest), so vanilla servers work.
    internal static class PbFeed
    {
        internal const string NameTag="[SEVR]";
        internal const string ArmsId="SevrArms";
        internal const double ServerInterval=1/30.0,ClientInterval=1/20.0,Heartbeat=1,ScanInterval=1;
        private sealed class Sent { internal string Body; internal double Time; internal long Controller; }

        private static readonly PbMessage message=new PbMessage();
        private static readonly HashSet<long> armed=new HashSet<long>();
        private static readonly Dictionary<long,Sent> sent=new Dictionary<long,Sent>();
        private static List<MyProgrammableBlock> targets=new List<MyProgrammableBlock>(),previous=new List<MyProgrammableBlock>();
        private static MyShipController scanned;
        private static MySession controlsSession;
        private static double nextScan,nextSend;
        private static uint sequence;
        private static bool reportedFailure;

        internal static readonly ActionChoice ArmsAction=new ActionChoice(()=>"VR arms: "+(AnyArmed ? "On":"Off"),ToggleArms,()=>targets.Count>0,historyKey:"VR arms");
        private static bool AnyArmed => targets.Exists(pb=>armed.Contains(pb.EntityId));
        private static void ToggleArms()
        {
            bool on=!AnyArmed;
            foreach(var pb in targets) SetArms(pb,on);
        }
        private static void SetArms(MyProgrammableBlock pb,bool on)
        {
            if(on) armed.Add(pb.EntityId); else armed.Remove(pb.EntityId);
            nextSend=0;
        }
        internal static bool Tagged(MyProgrammableBlock pb) => pb?.CustomName!=null && pb.CustomName.ToString().IndexOf(NameTag,StringComparison.Ordinal)>=0;

        internal static void Reset()
        {
            foreach(var pb in targets) Stop(pb);
            targets.Clear(); previous.Clear(); sent.Clear(); armed.Clear();
            scanned=null; controlsSession=null; nextScan=nextSend=0; reportedFailure=false;
            PbArms.Reset();
        }

        // Block terminal controls exist only after the first programmable block creates them; adding ours first would leave the native ones out.
        internal static void Refresh()
        {
            var session=MySession.Static;
            if(session==null || session==controlsSession || !MyTerminalControlFactory.AreControlsCreated<MyProgrammableBlock>()) return;
            controlsSession=session;
            foreach(var control in MyTerminalControlFactory.GetControls(typeof(MyProgrammableBlock)))
                if(control.Id==ArmsId) return;
            var check=new MyTerminalControlCheckbox<MyProgrammableBlock>(ArmsId,MyStringId.GetOrCompute("VR arms"),MyStringId.GetOrCompute("Arms flag in the VR data sent to this block (Space Engineers VR)."))
            {
                Getter=pb=>armed.Contains(pb.EntityId),
                Setter=SetArms,
                Visible=Tagged,
                SupportsMultipleBlocks=false
            };
            check.EnableAction(MyTerminalActionIcons.TOGGLE,new StringBuilder("VR arms"),new StringBuilder("On"),new StringBuilder("Off"),Tagged);
            check.EnableOnAction(MyTerminalActionIcons.ON,new StringBuilder("VR arms on"),new StringBuilder("On"),new StringBuilder("Off"),Tagged);
            check.EnableOffAction(MyTerminalActionIcons.OFF,new StringBuilder("VR arms off"),new StringBuilder("On"),new StringBuilder("Off"),Tagged);
            MyTerminalControlFactory.AddControl(check);
        }

        internal static void Update()
        {
            double now=MultiplayerRuntime.Now;
            var controller=Common.Config.ProgrammableBlockData && Main.VrActive && Main.WorldAvailable ? MySession.Static?.ControlledEntity as MyShipController:null;
            if(controller!=scanned || now>=nextScan) Scan(controller,now);
            if(targets.Count==0 || now<nextSend) return;
            nextSend=now+(Sync.IsServer ? ServerInterval:ClientInterval);
            Fill(controller);
            int flags=message.Flags;
            foreach(var pb in targets)
            {
                message.Flags=flags|(armed.Contains(pb.EntityId) ? PbMessage.Arms:0);
                string body=message.Body();
                if(sent.TryGetValue(pb.EntityId,out var last) && last.Body==body && now-last.Time<Heartbeat) continue;
                if(last==null) sent[pb.EntityId]=last=new Sent();
                last.Body=body; last.Time=now; last.Controller=message.Controller;
                Run(pb,PbMessage.Format(++sequence,body));
            }
        }

        private static void Scan(MyShipController controller,double now)
        {
            scanned=controller; nextScan=now+ScanInterval;
            var swap=previous; previous=targets; targets=swap; targets.Clear();
            if(controller?.CubeGrid!=null && !controller.Closed)
                foreach(var grid in MyCubeGridGroups.Static.Mechanical.GetGroupNodes(controller.CubeGrid))
                    foreach(var pb in grid.GetFatBlocks<MyProgrammableBlock>())
                        if(!pb.Closed && Tagged(pb) && ((Sandbox.ModAPI.IMyTerminalBlock)pb).HasPlayerAccess(MySession.Static.LocalPlayerId))
                            targets.Add(pb);
            foreach(var pb in previous)
                if(!targets.Contains(pb)) Stop(pb);
            previous.Clear();
        }

        private static void Stop(MyProgrammableBlock pb)
        {
            if(!sent.TryGetValue(pb.EntityId,out var last)) return;
            sent.Remove(pb.EntityId);
            if(pb.Closed) return;
            var stop=new PbMessage {Controller=last.Controller,Flags=PbMessage.Stopped};
            Run(pb,PbMessage.Format(++sequence,stop.Body()));
        }

        private static void Fill(MyShipController controller)
        {
            var controls=Controls.Static;
            message.Controller=controller.EntityId;
            message.Flags=0;
            var headset=Player.Player.Headset.pose;
            if(headset.isTracked) { message.Flags|=PbMessage.Head; message.HeadPose=Local(headset.deviceToAbsolute.matrix); }
            else message.HeadPose=Matrix.Identity;
            if(Hand(Player.Player.HandL,message.LeftHand,controls.LeftTriggerPressure,controls.LeftGripPressure,controls.ThrustLRFB)) message.Flags|=PbMessage.Left;
            if(Hand(Player.Player.HandR,message.RightHand,controls.PointerPressure,controls.RightGripPressure,controls.ThrustRotate)) message.Flags|=PbMessage.Right;
            var character=MySession.Static.LocalCharacter;
            if(Joints(character,Player.Player.HandL,message.LeftArm,-1)) message.Flags|=PbMessage.LeftJoints;
            if(Joints(character,Player.Player.HandR,message.RightArm,1)) message.Flags|=PbMessage.RightJoints;
        }
        private static bool Joints(MyCharacter character,Controller hand,PbMessage.Joints joints,float side)
        {
            if((message.Flags&PbMessage.Head)!=0 && hand.pose.isTracked)
            {
                var wrist=Local(CockpitHandPose.GripWrist(hand.GripTracking));
                joints.Wrist=wrist.Translation;
                if(PbArms.TrySolve(character,message.HeadPose,wrist,side,out joints.Shoulder,out joints.Elbow)) return true;
            }
            joints.Shoulder=joints.Elbow=joints.Wrist=Vector3.Zero;
            return false;
        }
        private static bool Hand(Controller hand,PbMessage.Hand data,Analog trigger,Analog grip,Analog stick)
        {
            // Menus own the triggers; a script fed the raw value would fire while the player turns wheel pages.
            bool menu=Player.InputRouter.RadialOpen || Main.MenuOpen;
            data.Trigger=menu ? 0:trigger.RawPosition.X; data.Grip=menu ? 0:grip.RawPosition.X; data.Stick=stick.RawPosition;
            Array.Copy(hand.Fingers.Curls,data.Curls,5);
            data.Pose=hand.pose.isTracked ? Local(hand.GripTracking):Matrix.Identity;
            return hand.pose.isTracked;
        }
        // The seated recenter puts the tracking origin at the seat's eye, facing the seat (SpatialUi.DeviceWorld).
        private static Matrix Local(Matrix tracking) => VrMath.Affine(tracking*Player.Player.PlayerToAbsolute.inverted);

        private static void Run(MyProgrammableBlock pb,string argument)
        {
            try { ((Sandbox.ModAPI.IMyProgrammableBlock)pb).Run(argument,UpdateType.Mod); }
            catch(Exception ex)
            {
                if(reportedFailure) return;
                reportedFailure=true;
                Logger.Warning(ex,"VR data could not be sent to programmable block "+pb.EntityId);
            }
        }
    }
}
