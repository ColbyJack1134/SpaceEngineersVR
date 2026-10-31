using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Blocks;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.Multiplayer;
using Sandbox.Game.World;
using Sandbox.ModAPI.Ingame;
using SpaceEngineersVR.Multiplayer;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Player.Control;
using VRageMath;

namespace SpaceEngineersVR.Plugin
{
    // A client's Run becomes a reliable server request (MyProgrammableBlock.SendRunProgramRequest), so vanilla servers work.
    internal static class PbFeed
    {
        internal const string NameTag="[SEVR]";
        // Simulation ticks between messages: 30 Hz on the server, 20 Hz for a client's server requests.
        internal const ulong ServerTicks=2,ClientTicks=3;
        internal const double Heartbeat=1,ScanInterval=1;
        private sealed class Sent { internal string Body; internal double Time; internal long Controller; }

        private static readonly PbMessage message=new PbMessage();
        private static readonly Dictionary<long,Sent> sent=new Dictionary<long,Sent>();
        private static List<MyProgrammableBlock> targets=new List<MyProgrammableBlock>(),previous=new List<MyProgrammableBlock>();
        private static MyShipController scanned;
        private static double nextScan;
        private static ulong nextSend;
        private static uint sequence;
        private static bool reportedFailure;

        internal static bool Tagged(MyProgrammableBlock pb) => pb?.CustomName!=null && pb.CustomName.ToString().IndexOf(NameTag,StringComparison.Ordinal)>=0;

        internal static void Reset()
        {
            foreach(var pb in targets) Stop(pb);
            targets.Clear(); previous.Clear(); sent.Clear();
            scanned=null; nextScan=nextSend=0; reportedFailure=false;
            PbArms.Reset();
        }

        internal static void Update()
        {
            double now=MultiplayerRuntime.Now;
            var controller=Common.Config.ProgrammableBlockData && Main.VrActive && Main.WorldAvailable ? MySession.Static?.ControlledEntity as MyShipController:null;
            if(controller!=scanned || now>=nextScan) Scan(controller,now);
            ulong tick=Sandbox.MySandboxGame.Static.SimulationFrameCounter;
            if(targets.Count==0 || tick<nextSend) return;
            nextSend=tick+(Sync.IsServer ? ServerTicks:ClientTicks);
            Fill(controller);
            string body=message.Body();
            foreach(var pb in targets)
            {
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
