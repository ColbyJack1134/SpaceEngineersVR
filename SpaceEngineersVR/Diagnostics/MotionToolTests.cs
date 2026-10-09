using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using HarmonyLib;
using Sandbox.Game.Components;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.Entities.Character.Components;
using Sandbox.Game.World;
using Sandbox.Game.Weapons;
using SpaceEngineersVR.Patches;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Plugin;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class MotionToolTests
    {
        private static MyCharacter character;
        private static object container;
        private static MyWelder tool;
        private static bool fault;
        private static int poses;
        private static IEnumerable<CodeInstruction> CharacterGetter(IEnumerable<CodeInstruction> instructions,MethodBase __originalMethod)
        {
            yield return new CodeInstruction(OpCodes.Ldsfld,AccessTools.Field(typeof(MotionToolTests),nameof(character)));
            yield return new CodeInstruction(OpCodes.Castclass,((MethodInfo)__originalMethod).ReturnType);
            yield return new CodeInstruction(OpCodes.Ret);
        }
        private static IEnumerable<CodeInstruction> ContainerGetter(IEnumerable<CodeInstruction> instructions,MethodBase __originalMethod)
        {
            yield return new CodeInstruction(OpCodes.Ldsfld,AccessTools.Field(typeof(MotionToolTests),nameof(container)));
            yield return new CodeInstruction(OpCodes.Castclass,((MethodInfo)__originalMethod).ReturnType);
            yield return new CodeInstruction(OpCodes.Ret);
        }
        private static bool Available(ref bool __result) { __result=true; return false; }
        private static IEnumerable<CodeInstruction> WeaponGetter(IEnumerable<CodeInstruction> instructions,MethodBase __originalMethod)
        {
            yield return new CodeInstruction(OpCodes.Ldsfld,AccessTools.Field(typeof(MotionToolTests),nameof(tool)));
            yield return new CodeInstruction(OpCodes.Castclass,((MethodInfo)__originalMethod).ReturnType);
            yield return new CodeInstruction(OpCodes.Ret);
        }
        private static bool CharacterState(ref bool __result) { __result=false; return false; }
        private static bool Pose()
        {
            poses++;
            if(fault) throw new InvalidOperationException("Injected tool pose failure");
            return false;
        }
        internal static void Run(Action<string> log)
        {
            var session=AccessTools.Field(typeof(MySession),"m_static");
            var previousSession=session.GetValue(null);
            var previousLog=VRage.Utils.MyLog.Default;
            var disabled=AccessTools.Field(typeof(MotionToolPatch),"disabled");
            var previousDisabled=disabled.GetValue(null);
            var harmony=new Harmony("SEVR.MotionTool.Tests");
            var methods=new List<MethodBase>();
            void Patch(MethodInfo method,string prefix=null,string transpiler=null)
            {
                harmony.Patch(method,prefix:prefix==null ? null:new HarmonyMethod(typeof(MotionToolTests),prefix),
                    transpiler:transpiler==null ? null:new HarmonyMethod(typeof(MotionToolTests),transpiler));
                methods.Add(method);
            }
            try
            {
                if(VRage.Utils.MyLog.Default==null) VRage.Utils.MyLog.Default=new VRage.Utils.MyLog();
                character=(MyCharacter)FormatterServices.GetUninitializedObject(typeof(MyCharacter));
                tool=(MyWelder)FormatterServices.GetUninitializedObject(typeof(MyWelder));
                session.SetValue(null,FormatterServices.GetUninitializedObject(typeof(MySession)));
                disabled.SetValue(null,false); fault=false; poses=0;
                Patch(AccessTools.PropertyGetter(typeof(Main),nameof(Main.VrActive)),nameof(Available));
                Patch(AccessTools.PropertyGetter(typeof(InputRouter),"TrackedItems"),nameof(Available));
                foreach(string name in new[] {"IsDead","IsSitting","IsOnLadder"})
                    Patch(AccessTools.PropertyGetter(typeof(MyCharacter),name),nameof(CharacterState));
                foreach(string name in new[] {"LocalCharacter","ControlledEntity"})
                    Patch(AccessTools.PropertyGetter(typeof(MySession),name),transpiler:nameof(CharacterGetter));
                Patch(AccessTools.PropertyGetter(typeof(MyCharacterWeaponPositionComponent),"Character"),transpiler:nameof(CharacterGetter));
                var containerProperty=AccessTools.DeclaredProperty(typeof(VRage.Game.Components.MyEntityComponentBase),"Container");
                container=new VRage.Game.Components.MyEntityComponentContainer(character);
                Patch(containerProperty.GetGetMethod(true),transpiler:nameof(ContainerGetter));
                Patch(AccessTools.PropertyGetter(typeof(MyCharacter),"CurrentWeapon"),transpiler:nameof(WeaponGetter));
                Patch(AccessTools.Method(typeof(WeaponHandling),nameof(WeaponHandling.TryPose)),nameof(Pose));

                var render=AccessTools.Method(typeof(HeldItemRenderPosePatch),"Prefix");
                var component=FormatterServices.GetUninitializedObject(typeof(MyRenderComponentSkinnedEntity));
                Require(MotionToolPatch.Eligible(character),"Healthy tracked item override was disabled");
                render.Invoke(null,new[] {component});
                Require(poses==1,"Healthy render override did not evaluate the tool pose");
                AccessTools.Method(typeof(WeaponHandling),"LocalPose").Invoke(null,new object[] {character});
                Require(poses==2,"Healthy sensor provider did not evaluate the tool pose");
                var position=FormatterServices.GetUninitializedObject(typeof(MyCharacterWeaponPositionComponent));
                fault=true;
                AccessTools.Method(typeof(MotionToolPatch),"Postfix").Invoke(null,new[] {position});
                Require((bool)disabled.GetValue(null) && poses==3,"Simulation fault did not disable the tracked item override");
                render.Invoke(null,new[] {component});
                Require(!MotionToolPatch.Eligible(character),"Render eligibility ignored the tool failure gate");
                foreach(string name in new[] {"LocalPose","LocalToolRay"})
                    Require(AccessTools.Method(typeof(WeaponHandling),name).Invoke(null,new object[] {character})==null,"Failed tool override supplied a local sensor pose or ray");
                AccessTools.Method(typeof(MotionToolPatch),"Postfix").Invoke(null,new[] {position});
                Require(poses==3,"A failed tool override re-entered pose evaluation");
            }
            finally
            {
                foreach(var method in methods) harmony.Unpatch(method,HarmonyPatchType.All,harmony.Id);
                session.SetValue(null,previousSession); disabled.SetValue(null,previousDisabled);
                VRage.Utils.MyLog.Default=previousLog;
                character=null; container=null; tool=null; fault=false; poses=0;
            }
            log("PASS tool recovery: caught simulation fault prevents render, simulation and local sensor pose re-entry");
        }
        private static void Require(bool value,string message) { if(!value) throw new Exception(message); }
    }
}
