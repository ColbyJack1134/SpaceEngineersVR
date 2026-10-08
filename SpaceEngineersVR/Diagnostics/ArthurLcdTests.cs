using System;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Sandbox.ModAPI;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using VRage.Game.ModAPI;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Plugin;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class ArthurLcdTests
    {
        private static void Require(bool value,string message) { if(!value) throw new Exception(message); }
        private class BaseScreen { public object Block => "base"; public int SurfaceIndex => 3; }
        private class InteractiveScreen:BaseScreen { public new string Block => "interactive"; }
        private sealed class Proxy : RealProxy
        {
            private readonly Func<string,object> value;
            internal Proxy(Type type,Func<string,object> value):base(type) { this.value=value; }
            public override IMessage Invoke(IMessage message)
            {
                var call=(IMethodCallMessage)message;
                return new ReturnMessage(value(call.MethodName),null,0,call.LogicalCallContext,call);
            }
        }
        private static void Occlusion(Action<string> log)
        {
            object Block() => new Proxy(typeof(IMyTerminalBlock),name=>throw new Exception("Unexpected block query: "+name)).GetTransparentProxy();
            IHitInfo Hit(object entity) => (IHitInfo)new Proxy(typeof(IHitInfo),name=>name=="get_HitEntity" ? entity:name=="get_Position" ? (object)Vector3D.Zero:throw new Exception("Unexpected ray query: "+name)).GetTransparentProxy();
            object Grid(object block)
            {
                var slim=new Proxy(typeof(IMySlimBlock),name=>name=="get_FatBlock" ? block:throw new Exception("Unexpected slim query: "+name)).GetTransparentProxy();
                return new Proxy(typeof(IMyCubeGrid),name=>name=="GetCubeBlock" ? slim:name=="WorldToGridInteger" ? (object)Vector3I.Zero:throw new Exception("Unexpected grid query: "+name)).GetTransparentProxy();
            }
            var screen=(IMyTerminalBlock)Block(); var other=Block();
            Require(ArthurLcdBridge.HitOwner(screen,Hit(screen)),"LCD housing blocked its own screen");
            Require(ArthurLcdBridge.HitOwner(screen,Hit(Grid(screen))),"Grid physics hit did not resolve the LCD owner");
            Require(!ArthurLcdBridge.HitOwner(screen,Hit(Grid(other))),"A different block stopped occluding the LCD");
            Require(!ArthurLcdBridge.HitOwner(screen,Hit(other)),"Unrelated hit entity was ignored");
            log("PASS Arthur LCD occlusion: own block/grid cell excluded, unrelated blocks/entities still occlude.");
        }
        internal static void Run(Action<string> log)
        {
            LcdInputTests.Run(log);
            Occlusion(log);
            var screen=new InteractiveScreen();
            Require((string)CockpitRender.Member(screen,"Block")=="interactive","Arthur hidden Block property resolved to the base property");
            Require((int)CockpitRender.Member(screen,"SurfaceIndex")==3,"Arthur inherited surface property lookup failed");
            Require((string)CockpitRender.Member(screen,"Block")=="interactive","Arthur cached hidden property lookup changed");
            log("PASS Arthur LCD member lookup: derived Block property with a different return type, inherited surface property and cached lookup.");
            for(int i=0;i<200;i++)
            {
                var block=MatrixD.CreateFromYawPitchRoll(i*.13,i*.07,i*.03);
                block.Translation=new Vector3D(2e6+i*100,-3e6,4e6);
                var moved=MatrixD.CreateRotationY(.4)*MatrixD.CreateTranslation(.2,.1,-.3)*block;
                var transform=ArthurLcdBridge.NativeRayFrame(moved,block);
                var local=new Vector3D(.12,-.08,.2);
                var native=Vector3D.Transform(Vector3D.Transform(local,moved),transform);
                Require(Vector3D.Distance(native,Vector3D.Transform(local,block))<1e-6,"Arthur ray missed relocated screen in a large moving world");
                var direction=Vector3D.TransformNormal(Vector3D.TransformNormal(Vector3D.Forward,moved),transform);
                Require(Vector3D.Distance(direction,Vector3D.TransformNormal(Vector3D.Forward,block))<1e-8,"Arthur relocated-screen ray direction changed");
            }
            log("PASS Arthur LCD ray transform: 200 translated/rotated screen frames in large moving worlds; origin and direction preserve native UV geometry.");
        }
        private static void Geometry(Type geometryType,Type readerType,string modelName,string[] materials,Action<string> log)
        {
            var model=VRage.Game.Models.MyModels.GetModelOnlyData(modelName);
            object geometry=null;
            string modelPath=Path.Combine(VRage.FileSystem.MyFileSystem.ContentPath,modelName);
            using(var reader=new BinaryReader(File.OpenRead(modelPath)))
            {
                object[] lods={reader,null};
                if((bool)AccessTools.Method(readerType,"TryReadLodPaths").Invoke(null,lods))
                {
                    var first=((System.Collections.Generic.List<string>)lods[1]).First();
                    string relative=first.EndsWith(".mwm",StringComparison.OrdinalIgnoreCase) ? first:first+".mwm";
                    modelPath=Path.Combine(VRage.FileSystem.MyFileSystem.ContentPath,relative);
                }
            }
            foreach(string material in materials)
            {
                using(var reader=new BinaryReader(File.OpenRead(modelPath)))
                {
                    object[] data={reader,material,null};
                    if((bool)AccessTools.Method(readerType,"TryReadScreenArea").Invoke(null,data)) { geometry=data[2]; break; }
                }
            }
            Require(geometry!=null,"Arthur could not read an installed LCD material: "+modelName);
            var matrixMethod=geometryType.GetMethods(BindingFlags.Static|BindingFlags.NonPublic).Single(m=>m.Name=="TryGetScreenLocalMatrix" && m.GetParameters().Length==3);
            object[] matrixArgs={model,geometry,Matrix.Identity};
            Require((bool)matrixMethod.Invoke(null,matrixArgs),"Arthur installed LCD plane failed");
            var local=(Matrix)matrixArgs[2];
            var intersect=geometryType.GetMethods(BindingFlags.Static|BindingFlags.NonPublic).Single(m=>m.Name=="TryGetScreenUvIntersection" && m.GetParameters().Length==6);
            for(int i=0;i<24;i++) for(int angle=-75;angle<=75;angle+=15)
            {
                var native=MatrixD.CreateFromYawPitchRoll(i*.07,i*.03,i*.02);
                native.Translation=new Vector3D(2e6,-3e6,4e6);
                var moved=MatrixD.CreateRotationY(.3)*MatrixD.CreateTranslation(.2,0,-.1)*native;
                var plane=(MatrixD)local*moved;
                var origin=plane.Translation+plane.Backward*.2+plane.Right*(.2*Math.Tan(MathHelper.ToRadians(angle)));
                var direction=Vector3D.Normalize(plane.Translation-origin);
                var transform=ArthurLcdBridge.NativeRayFrame(moved,native);
                object[] hit={model,native,geometry,Vector3D.Transform(origin,transform),Vector3D.TransformNormal(direction,transform),Vector2.Zero};
                Require((bool)intersect.Invoke(null,hit),"Arthur hand ray missed its native LCD after screen relocation");
                Require(Vector2.Distance((Vector2)hit[5],new Vector2(.5f))<.01f,"Arthur screen center UV moved after relocation");
            }
            log("PASS actual Arthur LCD geometry: "+modelName+";264 center rays across 24 large-world screen moves and -75 to +75 degree angles.");
        }
        internal static void RunNative(Action<string> log)
        {
            string path=Environment.GetEnvironmentVariable("SEVR_ARTHUR_LCD_FIXTURE");
            if(string.IsNullOrEmpty(path)) { log("Arthur LCD source fixture not requested (SEVR_ARTHUR_LCD_FIXTURE)."); return; }
            if(!File.Exists(path)) throw new FileNotFoundException("Arthur LCD source fixture",path);
            var assembly=Assembly.LoadFrom(path);
            var type=assembly.GetType("LcdMod.Client.Modules.EyeTracking.EyeTrackingModule",true);
            var geometryType=assembly.GetType("LcdMod.Client.ScreenAreas.ScreenAreaGeometry",true);
            var scriptType=assembly.GetType("LcdMod.Client.SurfaceScripts.Abstract.SurfaceScriptBase",true);
            Require(CockpitRender.Find(scriptType,"Block")?.DeclaringType==scriptType,"Arthur native hidden Block property did not resolve to SurfaceScriptBase");
            var readerType=assembly.GetType("LcdMod.Client.ScreenAreas.MinimalMwmReader",true);
            Geometry(geometryType,readerType,CockpitRig.Find(CockpitLayout.Fighter).Model,Enumerable.Range(1,9).Select(i=>"CockpitScreen_"+i.ToString("00")).ToArray(),log);
            Geometry(geometryType,readerType,"Models/Cubes/Large/LCDPanel.mwm",new[] {"ScreenArea"},log);
            var failed=AccessTools.Field(typeof(Main),"failed");
            bool previous=(bool)failed.GetValue(null);
            try
            {
                ArthurLcdBridge.Attach(type);
                Require(ArthurLcdBridge.Ready,"Arthur source-built adapter did not attach");
                foreach(string name in new[] {"Update","TryGetCameraRay","UpdateScrollState"})
                    Require(Harmony.GetPatchInfo(AccessTools.Method(type,name))?.Owners.Contains(Common.Plugin.Harmony.Id)==true,"Arthur interaction patch missing: "+name);
                failed.SetValue(null,false);
                object[] ray={Vector3D.One,Vector3D.One};
                Require(!(bool)AccessTools.Method(type,"TryGetCameraRay").Invoke(null,ray),"Arthur supplied a camera ray without a selected interactive LCD");
                foreach(string name in new[] {"HoldingClick","HoldingRightClick","HoldingMiddleClick","HoldingBackClick","HoldingForwardClick"})
                    Require(!(bool)AccessTools.Property(type,name).GetValue(null),"Arthur input leaked without an active screen: "+name);
                Require(AccessTools.Property(assembly.GetType("LcdMod.Client.LcdModClientComponent",true),"Instance")!=null,"Arthur live-session discovery contract changed");
                log("PASS actual Arthur LCD source fixture: adapter and native update transpiler attach, live-session contract, unselected ray/input rejection and unbound extra mouse buttons.");
                ArthurNativeInputTests.Run(assembly,type,log);
            }
            finally { ArthurLcdBridge.Reset(); failed.SetValue(null,previous); }
        }
    }
}
