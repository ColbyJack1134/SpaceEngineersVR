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
        private static Vector3 Transmit(MyShipController controller,Vector3 move,bool trigger,float throttle)
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
                Require(Transmit(controller,steering,true,0)==new Vector3(.6f,0,-1),"Trigger acceleration missing from native movement packet");
                Require(Transmit(controller,steering,false,.8f)==new Vector3(.6f,0,-1),"Handlebar acceleration missing from native movement packet");
                Require(Transmit(controller,Vector3.Zero,false,0)==Vector3.Zero,"Released throttle remained in native movement packet");
                Require(Transmit(controller,new Vector3(-.4f,1,.7f),false,0)==new Vector3(-.4f,1,.7f) && DriveInput.Braking,"Reverse, steering or braking input changed");
                selected=(MyToolbarItemTerminalBlock)FormatterServices.GetUninitializedObject(typeof(MyToolbarItemTerminalBlock));
                Require(Transmit(controller,steering,true,0)==new Vector3(.6f,0,0),"Selected toolbar trigger accelerated vehicle");
                selected=null;
                wheelCount=0;
                Require(Transmit(controller,steering,true,.8f)==steering,"Ship without wheels received vehicle throttle");
                wheelCount=4; wheelsEnabled=false;
                Require(Transmit(controller,steering,true,.8f)==steering,"Disabled wheel control received vehicle throttle");
                wheelsEnabled=true; shipEnabled=false;
                Require(Transmit(controller,steering,true,.8f)==steering,"Disabled ship control received vehicle throttle");
                shipEnabled=true; mainCockpit=false; otherMain=true;
                Require(Transmit(controller,steering,true,.8f)==steering,"Inactive cockpit received vehicle throttle");
                otherMain=false;
                Require(Transmit(controller,steering,true,0)==new Vector3(.6f,0,-1),"Cockpit without a designated main lost throttle");
                DriveInput.Stop();
                Require(!DriveInput.Braking,"Reset retained braking input");
            }
            finally
            {
                foreach(var method in methods) harmony.Unpatch(method,HarmonyPatchType.All,harmony.Id);
                wheels=null; grid=null; toolbar=null; selected=null; DriveInput.Stop();
            }
            log("PASS native vehicle input: trigger/handlebar throttle, release, reverse, steering, braking and eligibility through native movement and packet serialization");
        }
        private static void Require(bool value,string message) { if(!value) throw new Exception(message); }
    }
}
