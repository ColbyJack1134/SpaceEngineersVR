using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using SpaceEngineersVR.Plugin;
using System.Reflection;
using System.Runtime.ExceptionServices;
using HarmonyLib;
using SpaceEngineersVR.Player;
using VRage.Game.ModAPI;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class OptionalLcdTests
    {
        internal static void Run(Action<string> log)
        {
            bool memberException=false;
            EventHandler<FirstChanceExceptionEventArgs> observe=(sender,args)=> { if(args.Exception is MemberAccessException) memberException=true; };
            var before=new HashSet<MethodBase>(Harmony.GetAllPatchedMethods());
            bool touchReady=TouchScreenBridge.Ready,arthurReady=ArthurLcdBridge.Ready;
            AppDomain.CurrentDomain.FirstChanceException+=observe;
            try
            {
                Require(TouchScreenBridge.ContractSupported(typeof(TouchSession),typeof(TouchScreen),typeof(TouchInput)),"Supported touchscreen contract rejected");
                foreach(var screen in new[] {typeof(object),typeof(ReadOnlyScreen),typeof(WrongButtonScreen),typeof(WrongCoordsScreen),typeof(WrongUpdateScreen)})
                    Require(!TouchScreenBridge.ContractSupported(typeof(TouchSession),screen,typeof(TouchInput)),"Unsupported touchscreen layout accepted: "+screen.Name);
                Require(!TouchScreenBridge.ContractSupported(typeof(TouchSession),typeof(TouchScreen),typeof(WrongInput)),"Wrong touchscreen blacklist signature accepted");
                Require(!TouchScreenBridge.ContractSupported(typeof(WrongSession),typeof(TouchScreen),typeof(TouchInput)),"Missing touchscreen singleton accepted");
                Require(!TouchScreenBridge.ContractSupported(typeof(ReadOnlySession),typeof(TouchScreen),typeof(TouchInput)),"Read-only current screen accepted");
                Require(Arthur(typeof(EyeModule),typeof(Geometry),typeof(InputBlock)),"Supported Arthur contract rejected");
                foreach(var type in new[] {typeof(object),typeof(WrongClickModule),typeof(WrongCameraModule),typeof(WrongCollectionModule)})
                    Require(!Arthur(type,typeof(Geometry),typeof(InputBlock)),"Unsupported Arthur module accepted: "+type.Name);
                Require(!Arthur(typeof(EyeModule),typeof(WrongGeometry),typeof(InputBlock)),"Wrong Arthur intersection signature accepted");
                Require(!Arthur(typeof(EyeModule),typeof(Geometry),typeof(WrongInputBlock)),"Wrong Arthur input-block signature accepted");
                Require(!ArthurLcdBridge.ContractSupported(typeof(EyeModule),null,typeof(Geometry),typeof(InputBlock),typeof(IEye),typeof(Control)),"Missing Arthur script type accepted");
                // These assemblies lack the optional mod types. Attachment must return before any patch.
                Require(!TouchScreenBridge.Attach(typeof(TouchSession)),"Incomplete touchscreen assembly attached");
                ArthurLcdBridge.Attach(typeof(EyeModule));
                Require(before.SetEquals(Harmony.GetAllPatchedMethods()) && TouchScreenBridge.Ready==touchReady && ArthurLcdBridge.Ready==arthurReady,
                    "Rejected optional adapter changed patches or readiness");
                Require(!memberException,"Optional LCD contract discovery raised a first-chance member exception");
            }
            finally { AppDomain.CurrentDomain.FirstChanceException-=observe; }
            log("PASS optional LCD contracts: valid shapes retained; missing types, setters, fields and changed signatures skipped without first-chance member exceptions or patches");
            TouchAttachment(log);
        }
        private static bool failBlacklist;
        private static bool Blacklist()
        {
            if(failBlacklist) throw new InvalidOperationException("Injected touchscreen initialization failure");
            return false;
        }
        private static void TouchAttachment(Action<string> log)
        {
            string path=Environment.GetEnvironmentVariable("SEVR_TOUCH_API_FIXTURE");
            if(string.IsNullOrEmpty(path)) return;
            var assembly=Assembly.LoadFrom(path);
            var session=assembly.GetType("Lima.Touch.TouchSession",true);
            var instance=AccessTools.Field(session,"Instance"); var previous=instance.GetValue(null);
            var managerField=AccessTools.Field(session,"TouchMan");
            var manager=Activator.CreateInstance(managerField.FieldType);
            var native=FormatterServices.GetUninitializedObject(session); managerField.SetValue(native,manager);
            var screen=assembly.GetType("Lima.Touch.TouchScreen",true);
            var input=assembly.GetType("Lima.Utils.InputUtils",true);
            var blacklist=AccessTools.Method(input,"SetPlayerUseBlacklistState");
            var update=AccessTools.Method(managerField.FieldType,"UpdateAtSimulation"); var buttons=AccessTools.Method(screen,"UpdateMouseButtons");
            var harmony=new Harmony("SEVR.OptionalLcd.Tests");
            bool Owned(MethodBase method) => Harmony.GetPatchInfo(method)?.Owners.Contains(Common.Plugin.Harmony.Id)==true;
            Require(!TouchScreenBridge.Ready,"Touchscreen fixture requires an idle adapter");
            try
            {
                harmony.Patch(blacklist,prefix:new HarmonyMethod(typeof(OptionalLcdTests),nameof(Blacklist)));
                instance.SetValue(null,native);
                Require(TouchScreenBridge.Attach(session) && TouchScreenBridge.Ready && Owned(update) && Owned(buttons),"Source touchscreen attachment failed");
                TouchScreenBridge.Reset();
                Require(!Owned(update) && !Owned(buttons),"Touchscreen reset left adapter patches");
                failBlacklist=true;
                bool failed=false;
                try { TouchScreenBridge.Attach(session); }
                catch(TargetInvocationException ex) when(ex.InnerException is InvalidOperationException) { failed=true; }
                Require(failed && !TouchScreenBridge.Ready && !Owned(update) && !Owned(buttons),"Failed touchscreen initialization retained partial patches");
            }
            finally
            {
                failBlacklist=false; TouchScreenBridge.Reset(); instance.SetValue(null,previous);
                harmony.Unpatch(blacklist,HarmonyPatchType.All,harmony.Id);
            }
            log("PASS actual touchscreen attachment: source contract accepted, reset unpatches and injected initialization failure rolls back both hooks");
        }
        private static bool Arthur(Type module,Type geometry,Type input) => ArthurLcdBridge.ContractSupported(module,typeof(Script),geometry,input,typeof(IEye),typeof(Control));
        private static void Require(bool condition,string message) { if(!condition) throw new Exception(message); }
        private sealed class TouchSession
        {
            public static TouchSession Instance=null;
            public TouchManager TouchMan=new TouchManager();
            public bool ModEnabled => true;
        }
        private sealed class WrongSession
        {
            public WrongSession Instance => this;
            public TouchManager TouchMan=new TouchManager();
            public bool ModEnabled => true;
        }
        private sealed class ReadOnlySession
        {
            public static ReadOnlySession Instance=null;
            public ReadOnlyManager TouchMan=new ReadOnlyManager();
            public bool ModEnabled => true;
        }
        private class TouchManager
        {
            public readonly List<TouchScreen> Screens=new List<TouchScreen>();
            public TouchScreen CurrentScreen=null;
            public void UpdateAtSimulation() { }
        }
        private sealed class ReadOnlyManager:TouchManager { public new readonly TouchScreen CurrentScreen=null; }
        private sealed class Coords
        {
            public Vector3 TopLeft=Vector3.Zero,BottomLeft=Vector3.Zero,BottomRight=Vector3.Zero;
        }
        private sealed class Button
        {
            public void Update(bool first,bool active) { }
            public void Update(int unrelated) { }
        }
        private class TouchScreen
        {
            public IMyCubeBlock Block => null;
            public int Index => 0;
            public bool Enabled => true;
            public Coords Coords => null;
            public bool IsPlayerAiming { get; private set; }
            public bool IsOnScreen => false;
            public Vector3D Intersection { get; private set; }
            public Vector2 CursorPosition => Vector2.Zero;
            public float InteractiveDistance => 3;
            public Button Mouse1=new Button(),Mouse2=new Button(),Mouse3=new Button();
            public void UpdateAtSimulation() { }
            public Vector2 UpdateScreenCoord() => Vector2.Zero;
            public void UpdateMouseButtons() { }
        }
        private sealed class ReadOnlyScreen:TouchScreen { public new Vector3D Intersection => Vector3D.Zero; }
        private sealed class WrongButtonScreen:TouchScreen { public new object Mouse2=new object(); }
        private sealed class WrongCoordsScreen:TouchScreen { public new object Coords => null; }
        private sealed class WrongUpdateScreen:TouchScreen { public new int UpdateScreenCoord() => 0; }
        private static class TouchInput { public static void SetPlayerUseBlacklistState(bool value) { } }
        private static class WrongInput { public static void SetPlayerUseBlacklistState(int value) { } }
        private interface IEye { void MouseScroll(int value); }
        private sealed class Control { }
        private class Script
        {
            public IMyCubeBlock Block => null;
            public long LastRunTick => 0;
            public int RotationOrSurfaceIndex => 0;
        }
        private class EyeModule
        {
            public readonly HashSet<IEye> _modules=new HashSet<IEye>();
            public readonly List<IEye> _pendingModules=new List<IEye>();
            public void Update() { }
            public void UpdateClickState(Control control,IEye target,IEye looking) { }
            public static bool TryGetCameraRay(out Vector3D origin,out Vector3D direction) { origin=direction=Vector3D.Zero; return false; }
            public static bool HoldingClick => false;
            public static bool HoldingRightClick => false;
            public static bool HoldingMiddleClick => false;
            public static bool HoldingBackClick => false;
            public static bool HoldingForwardClick => false;
            public static void UpdateScrollState(IEye target) { }
        }
        private sealed class WrongClickModule:EyeModule { public new static int HoldingForwardClick => 0; }
        private sealed class WrongCameraModule:EyeModule
        {
            public new static int TryGetCameraRay(out Vector3D origin,out Vector3D direction) { origin=direction=Vector3D.Zero; return 0; }
        }
        private sealed class WrongCollectionModule:EyeModule { public new object _pendingModules=new object(); }
        private class Geometry
        {
            public static bool TryGetScreenPointIntersection(Script screen,Vector3D origin,Vector3D direction,out Vector2 hit) { hit=Vector2.Zero; return false; }
            public static bool TryGetScreenLocalMatrix(Script screen,out Matrix matrix) { matrix=Matrix.Identity; return false; }
        }
        private sealed class WrongGeometry:Geometry
        {
            public new static int TryGetScreenPointIntersection(Script screen,Vector3D origin,Vector3D direction,out Vector2 hit) { hit=Vector2.Zero; return 0; }
        }
        private class InputBlock
        {
            public void Update() { }
            public void SetInputBlocked(bool value) { }
        }
        private sealed class WrongInputBlock:InputBlock { public new int SetInputBlocked(bool value) => 0; }
    }
}
