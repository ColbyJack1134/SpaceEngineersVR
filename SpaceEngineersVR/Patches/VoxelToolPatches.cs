using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.SessionComponents;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Plugin;
using VRage.Input;
using VRage.Game.Utils;
using VRage.Utils;
using VRageMath;

namespace SpaceEngineersVR.Patches
{
    // Voxel tools aim along the head; inside their update they aim along the building finger ray instead.
    internal static class VoxelAim
    {
        [ThreadStatic] internal static VoxelTool Scope;
        internal static bool TryPose(out MatrixD pose)
        {
            pose=MatrixD.Identity;
            // TryBuilderPose keeps the last pose while the wheel or a menu is open, like the building preview.
            return Scope!=null && Main.VrActive && !SpectatorView.Active && ReferenceEquals(VoxelTools.Current,Scope) &&
                PlacementControls.TryBuilderPose(out pose);
        }
        internal static Vector3 Forward(MyCamera camera) => TryPose(out var pose) ? (Vector3)Vector3D.Normalize(pose.Forward) : camera.ForwardVector;
        internal static MatrixD View(MyCamera camera) => TryPose(out var pose) ? MatrixD.Invert(VrMath.Rigid(pose)) : camera.ViewMatrix;
    }

    [HarmonyPatch(typeof(MyCharacter),nameof(MyCharacter.GetHeadMatrix))]
    internal static class VoxelAimHeadPatch
    {
        private static void Postfix(ref MatrixD __result) { if(VoxelAim.TryPose(out var pose)) __result=pose; }
    }

    [HarmonyPatch(typeof(MySessionComponentVoxelHand),nameof(MySessionComponentVoxelHand.UpdateBeforeSimulation))]
    internal static class VoxelHandAimPatch
    {
        private static void Prefix(out VoxelTool __state) { __state=VoxelAim.Scope; VoxelAim.Scope=VoxelTools.Hand; }
        private static void Finalizer(VoxelTool __state) => VoxelAim.Scope=__state;
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions,ILGenerator generator)
        {
            var forward=AccessTools.PropertyGetter(typeof(MyCamera),nameof(MyCamera.ForwardVector));
            var view=AccessTools.Field(typeof(MyCamera),nameof(MyCamera.ViewMatrix));
            int forwards=0,views=0;
            LocalBuilder temp=null;
            foreach(var instruction in instructions)
            {
                if(instruction.Calls(forward))
                {
                    forwards++;
                    yield return new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(VoxelAim),nameof(VoxelAim.Forward))).MoveLabelsFrom(instruction);
                }
                else if((instruction.opcode==OpCodes.Ldfld || instruction.opcode==OpCodes.Ldflda) && Equals(instruction.operand,view))
                {
                    views++;
                    yield return new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(VoxelAim),nameof(VoxelAim.View))).MoveLabelsFrom(instruction);
                    if(instruction.opcode==OpCodes.Ldflda)
                    {
                        temp=temp ?? generator.DeclareLocal(typeof(MatrixD));
                        yield return new CodeInstruction(OpCodes.Stloc,temp);
                        yield return new CodeInstruction(OpCodes.Ldloca,temp);
                    }
                }
                else yield return instruction;
            }
            // Brush placement, surface projection and the brush depth along the view.
            if(forwards!=2 || views!=1) throw new InvalidOperationException("Native voxel hand aim changed: forward="+forwards+" view="+views);
        }
    }

    // Mods read controls through IMyControl as well as the game control methods.
    [HarmonyPatch]
    internal static class NativeControlPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach(string name in new[] {nameof(MyControl.IsPressed),nameof(MyControl.IsNewPressed),nameof(MyControl.IsNewReleased)})
                yield return AccessTools.Method(typeof(MyControl),name,Type.EmptyTypes);
        }
        private static void Postfix(MyControl __instance,MethodBase __originalMethod,ref bool __result)
        {
            if(__result || !Main.VrActive) return;
            var type=__originalMethod.Name==nameof(MyControl.IsPressed) ? MyControlStateType.PRESSED :
                __originalMethod.Name==nameof(MyControl.IsNewReleased) ? MyControlStateType.NEW_RELEASED : MyControlStateType.NEW_PRESSED;
            __result=NativeActions.Read(__instance.GetGameControlEnum(),type);
        }
    }

    // Digi tools read modifier keys through the mod API; the shared controls supply them while the tool updates.
    [HarmonyPatch]
    internal static class DigiModifierPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            var input=AccessTools.TypeByName("VRage.Input.MyVRageInput");
            if(input==null) yield break;
            foreach(string name in new[] {"IsAnyCtrlKeyPressed","IsAnyShiftKeyPressed","IsAnyAltKeyPressed"})
            {
                var method=AccessTools.Method(input,"VRage.ModAPI.IMyInput."+name,Type.EmptyTypes);
                if(method!=null) yield return method;
            }
        }
        private static void Postfix(MethodBase __originalMethod,ref bool __result)
        {
            if(__result || !(VoxelAim.Scope is DigiTool tool) || !Main.VrActive || !ReferenceEquals(VoxelTools.Current,tool)) return;
            string name=__originalMethod.Name;
            if(name.EndsWith("IsAnyCtrlKeyPressed")) __result=tool.Removing && InputRouter.Gameplay && !Main.MenuOpen;
            else if(VoxelTools.LockPending && NativeActions.Read(Sandbox.Game.MyControlsSpace.FREE_ROTATION,MyControlStateType.PRESSED))
                __result=name.EndsWith(tool.Terrain ? "IsAnyAltKeyPressed":"IsAnyShiftKeyPressed");
        }
    }

    // Binds to Digi's Concrete Tool and its forks when their scripts load.
    internal static class DigiTools
    {
        private static readonly List<MethodBase> patched=new List<MethodBase>();
        private static readonly HashSet<Type> attempted=new HashSet<Type>();
        private static DateTime retry;
        private static readonly HashSet<Assembly> scanned=new HashSet<Assembly>();
        internal static void RefreshRegistrations()
        {
            if(!Main.VrActive || Sandbox.Game.World.MySession.Static==null || DateTime.UtcNow<retry) return;
            retry=DateTime.UtcNow.AddSeconds(1);
            foreach(var assembly in Util.Util.NewestAssemblies())
                if(scanned.Add(assembly))
                    foreach(var type in ToolTypes(assembly)) if(attempted.Add(type)) Attach(type);
        }
        // Only Digi script assemblies are enumerated; other assemblies can hold types that throw when inspected.
        internal static List<Type> ToolTypes(Assembly assembly)
        {
            var result=new List<Type>();
            Type[] types;
            try
            {
                if(assembly.IsDynamic || assembly.GetType("Digi.InputHandler",false)==null) return result;
                types=assembly.GetTypes();
            }
            catch(ReflectionTypeLoadException ex) { types=ex.Types.Where(t=>t!=null).ToArray(); }
            catch { return result; }
            foreach(var type in types)
            {
                try
                {
                    if(type.Namespace=="Digi.ConcreteTool" && typeof(VRage.Game.Components.MySessionComponentBase).IsAssignableFrom(type)) result.Add(type);
                }
                catch { }
            }
            return result;
        }
        private static void Attach(Type type)
        {
            var holding=AccessTools.Method(type,"HoldingTool",new[] {typeof(VRage.Game.ModAPI.IMyCharacter)});
            var input=type.Assembly.GetType("Digi.InputHandler",false);
            var paint=input==null ? null:AccessTools.Method(input,"IsControlPressedIgnoreBlock",new[] {typeof(MyStringId),typeof(bool)});
            var tool=holding==null || paint==null ? null:DigiTool.TryCreate(type);
            if(tool==null) return;
            int start=patched.Count;
            try
            {
                Patch(holding,new HarmonyMethod(typeof(DigiTools),nameof(Begin)),finalizer:new HarmonyMethod(typeof(DigiTools),nameof(End)));
                Patch(paint,postfix:new HarmonyMethod(typeof(DigiTools),nameof(Paint)));
                VoxelTools.Mods.Add(tool);
                Logger.Info("Voxel tool adapter attached to "+type.FullName+" in "+type.Assembly.GetName().Name);
            }
            catch(Exception ex) { Rollback(start); Logger.Warning(ex,"Voxel tool adapter unavailable for "+type.FullName); }
        }
        private static void Patch(MethodBase method,HarmonyMethod prefix=null,HarmonyMethod postfix=null,HarmonyMethod finalizer=null)
        {
            Common.Plugin.Harmony.Patch(method,prefix,postfix,finalizer:finalizer);
            patched.Add(method);
        }
        private static void Rollback(int start)
        {
            foreach(var method in patched.Skip(start).ToArray())
                foreach(string name in new[] {nameof(Begin),nameof(End),nameof(Paint)})
                    Common.Plugin.Harmony.Unpatch(method,AccessTools.Method(typeof(DigiTools),name));
            patched.RemoveRange(start,patched.Count-start);
        }
        internal static void Reset()
        {
            Rollback(0); attempted.Clear(); scanned.Clear(); VoxelTools.Mods.Clear(); retry=DateTime.MinValue;
        }
        private static void Begin(object __instance,out VoxelTool __state)
        {
            __state=VoxelAim.Scope;
            VoxelAim.Scope=VoxelTools.Mods.FirstOrDefault(tool=>tool.Type==__instance.GetType());
        }
        private static Exception End(Exception __exception,VoxelTool __state) { VoxelAim.Scope=__state; return __exception; }
        private static void Paint(MyStringId controlId,bool newPress,ref bool __result)
        {
            if(!__result && Main.VrActive) __result=NativeActions.Read(controlId,newPress ? MyControlStateType.NEW_PRESSED:MyControlStateType.PRESSED);
        }
    }
}
