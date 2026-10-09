using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using Sandbox.Game.Entities;
using Sandbox.Game.GameSystems;
using Sandbox.Game.Replication.ClientStates;
using Sandbox.Game.Screens.Helpers;
using SpaceEngineersVR.Player;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class DriveInputTests
    {
        private static bool wheelsEnabled,shipEnabled,mainCockpit,otherMain;
        private static int wheelCount;
        private static MyGridWheelSystem wheels;
        private static MyCubeGrid grid;
        private static MyToolbar toolbar;
        private static MyToolbarItem selected;
        private static Vector3 sent;
        private static bool Flags(MethodBase __originalMethod,ref bool __result)
        {
            switch(__originalMethod.Name)
            {
                case "get_ControlWheels": __result=wheelsEnabled; break;
                case "get_EnableShipControl": __result=shipEnabled; break;
                case "get_IsMainCockpit": __result=mainCockpit; break;
                default: __result=otherMain; break;
            }
            return false;
        }
        private static bool Wheels(ref MyGridWheelSystem __result) { __result=wheels; return false; }
        private static bool Grid(ref MyCubeGrid __result) { __result=grid; return false; }
        private static bool Count(ref int __result) { __result=wheelCount; return false; }
        private static bool Toolbar(ref MyToolbar __result) { __result=toolbar; return false; }
        private static bool Selection(ref MyToolbarItem __result) { __result=selected; return false; }
        private static bool Movement(Vector3 moveIndicator) { sent=moveIndicator; return false; }
        private static Vector3 Transmit(MyShipController controller,Vector3 move,float trigger,float throttle)
        {
            var command=DriveInput.Update(controller,move,trigger,throttle);
            ((VRage.Game.ModAPI.Interfaces.IMyControllableEntity)controller).MoveAndRotate(command,Vector2.Zero,0);
            var state=new MyGridClientState {Move=sent,Rotation=new Vector2(.2f,-.4f),Roll=.6f};
            var streamType=Assembly.Load("VRage.Library").GetType("VRage.Library.Collections.BitStream",true);
            using(var stream=(IDisposable)Activator.CreateInstance(streamType,new object[] {64}))
            {
                streamType.GetMethod("ResetWrite",Type.EmptyTypes).Invoke(stream,null);
                typeof(MyGridClientState).GetMethod("Serialize").Invoke(state,new object[] {stream});
                streamType.GetMethod("ResetRead",Type.EmptyTypes).Invoke(stream,null);
                var received=(MyGridClientState)Activator.CreateInstance(typeof(MyGridClientState),new object[] {stream});
                Require(received.Valid && received.Rotation==state.Rotation && received.Roll==state.Roll,"Native vehicle packet lost rotation input");
                return received.Move;
            }
        }
        internal static void Run(Action<string> log)
        {
            var harmony=new Harmony("SEVR.DriveInput.Tests");
            var methods=new List<MethodBase>();
            void Patch(MethodBase method,string prefix)
            {
                harmony.Patch(method,prefix:new HarmonyMethod(typeof(DriveInputTests),prefix));
                methods.Add(method);
            }
            try
            {
                var controller=(MyCockpit)FormatterServices.GetUninitializedObject(typeof(MyCockpit));
                wheels=(MyGridWheelSystem)FormatterServices.GetUninitializedObject(typeof(MyGridWheelSystem));
                grid=(MyCubeGrid)FormatterServices.GetUninitializedObject(typeof(MyCubeGrid));
                toolbar=(MyToolbar)FormatterServices.GetUninitializedObject(typeof(MyToolbar));
                wheelCount=4; wheelsEnabled=shipEnabled=mainCockpit=true; otherMain=false; selected=null;
                foreach(string name in new[] {"ControlWheels","EnableShipControl","IsMainCockpit"})
                    Patch(AccessTools.PropertyGetter(typeof(MyShipController),name),nameof(Flags));
                Patch(AccessTools.Method(typeof(MyCubeGrid),"HasMainCockpit"),nameof(Flags));
                Patch(AccessTools.PropertyGetter(typeof(MyCubeBlock),"CubeGrid"),nameof(Grid));
                Patch(AccessTools.PropertyGetter(typeof(MyShipController),"GridWheels"),nameof(Wheels));
                Patch(AccessTools.PropertyGetter(typeof(MyGridWheelSystem),"WheelCount"),nameof(Count));
                Patch(AccessTools.PropertyGetter(typeof(MyShipController),"Toolbar"),nameof(Toolbar));
                Patch(AccessTools.PropertyGetter(typeof(MyToolbar),"SelectedItem"),nameof(Selection));
                Patch(AccessTools.Method(typeof(MyShipController),"MoveAndRotate",new[] {typeof(Vector3),typeof(Vector2),typeof(float)}),nameof(Movement));
                var steering=new Vector3(.6f,0,-.3f);
                Require(Transmit(controller,steering,1,0)==new Vector3(.6f,0,-1),"Trigger acceleration missing from native movement packet");
                Require(Transmit(controller,steering,0,.8f)==new Vector3(.6f,0,-.8f),"Analog handlebar acceleration missing from native movement packet");
                Require(Transmit(controller,steering,0,0)==new Vector3(.6f,0,0),"Native wheel stick drift suppression changed");
                Require(Transmit(controller,Vector3.Zero,0,0)==Vector3.Zero,"Released throttle remained in native movement packet");
                Require(Transmit(controller,new Vector3(-.4f,1,.7f),0,0)==new Vector3(-.4f,1,.7f) && DriveInput.Braking,"Reverse, steering or braking input changed");
                selected=(MyToolbarItemTerminalBlock)FormatterServices.GetUninitializedObject(typeof(MyToolbarItemTerminalBlock));
                Require(Transmit(controller,steering,1,0)==new Vector3(.6f,0,0),"Selected toolbar trigger accelerated vehicle");
                Require(Transmit(controller,steering,1,.4f)==new Vector3(.6f,0,-.4f),"Selected toolbar suppressed independent wrist throttle");
                selected=null;
                wheelCount=0;
                Require(Transmit(controller,steering,.55f,0)==new Vector3(.6f,0,-.55f),"Wheel-less trigger gas missing from native movement");
                Require(Transmit(controller,steering,0,.65f)==new Vector3(.6f,0,-.65f),"Wheel-less wrist gas missing from native movement");
                foreach(float z in new[] {-.3f,.3f})
                {
                    var partial=new Vector3(.4f,.2f,z);
                    Require(Transmit(controller,partial,0,0)==partial,"Wheel-less analog native thrust was filtered as wheel drift");
                }
                selected=(MyToolbarItemTerminalBlock)FormatterServices.GetUninitializedObject(typeof(MyToolbarItemTerminalBlock));
                Require(Transmit(controller,steering,1,0)==steering,"Wheel-less selected-weapon trigger added thrust");
                Require(Transmit(controller,steering,1,.6f)==new Vector3(.6f,0,-.6f),"Wheel-less selected weapon suppressed wrist thrust");
                selected=null;
                wheelCount=4; wheelsEnabled=false;
                Require(Transmit(controller,steering,.7f,0)==new Vector3(.6f,0,-.7f),"Disabled wheel control blocked trigger thrust");
                Require(Transmit(controller,steering,0,.8f)==new Vector3(.6f,0,-.8f),"Disabled wheel control blocked wrist thrust");
                Require(Transmit(controller,steering,0,0)==steering,"Disabled wheel control still filtered native analog thrust");
                wheelsEnabled=true; shipEnabled=false;
                Require(Transmit(controller,steering,1,.8f)==new Vector3(.6f,0,-1),"Plugin filtered gas before native ship-control eligibility");
                shipEnabled=true; mainCockpit=false; otherMain=true;
                Require(Transmit(controller,steering,1,.8f)==new Vector3(.6f,0,-1),"Plugin filtered gas before native main-cockpit eligibility");
                otherMain=false;
                Require(Transmit(controller,steering,1,0)==new Vector3(.6f,0,-1),"Cockpit without a designated main lost throttle");
                wheelsEnabled=false;
                var forward=new Vector3(-.4f,0,-.9f);
                Require(Transmit(controller,forward,.5f,.7f)==forward,"Added throttle reduced stronger native forward thrust");
                Require(Transmit(controller,new Vector3(.1f,0,.6f),.5f,.7f)==new Vector3(.1f,0,-.7f),"Forward throttle did not override reverse native input");
                Require(Transmit(controller,Vector3.Zero,2,-1)==new Vector3(0,0,-1),"Forward gas did not clamp to native range");
                Require(Transmit(controller,Vector3.Zero,float.NaN,float.PositiveInfinity)==Vector3.Zero,"Nonfinite gas reached native movement");
                wheels=null;
                Require(Transmit(controller,Vector3.Zero,0,.35f)==new Vector3(0,0,-.35f),"Absent wheel system blocked wrist thrust");
                Require(Transmit(controller,Vector3.Zero,0,0)==Vector3.Zero,"Wheel-less throttle did not release through native packet");
                DriveInput.Stop();
                Require(!DriveInput.Braking,"Reset retained braking input");
            }
            finally
            {
                foreach(var method in methods) harmony.Unpatch(method,HarmonyPatchType.All,harmony.Id);
                wheels=null; grid=null; toolbar=null; selected=null; DriveInput.Stop();
            }
            log("PASS native forward input: analog trigger/wrist gas without wheels or wheel control, wheel-only stick filtering, firing isolation, native eligibility, release and client packet serialization");
        }
        private static void Require(bool value,string message) { if(!value) throw new Exception(message); }
    }
}
