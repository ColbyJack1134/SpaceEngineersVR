#if SEVR_DIAGNOSTICS
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using HarmonyLib;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.Gui;
using Sandbox.Game.World;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Plugin;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class HudLifecycleTests
    {
        private static MyCharacter character;
        private static object controlled;
        private static bool dead;
        private static bool Dead(ref bool __result) { __result=dead; return false; }
        private static IEnumerable<CodeInstruction> Getter(IEnumerable<CodeInstruction> instructions,MethodBase __originalMethod)
        {
            string field=__originalMethod.Name=="get_LocalCharacter" ? nameof(character):nameof(controlled);
            yield return new CodeInstruction(OpCodes.Ldsfld,AccessTools.Field(typeof(HudLifecycleTests),field));
            yield return new CodeInstruction(OpCodes.Castclass,((MethodInfo)__originalMethod).ReturnType);
            yield return new CodeInstruction(OpCodes.Ret);
        }
        internal static void Run(Action<string> log)
        {
            var session=AccessTools.Field(typeof(MySession),"m_static");
            var snapshot=AccessTools.Field(typeof(EssentialHud),"snapshot");
            var sample=AccessTools.Field(typeof(EssentialHud),"nextSample");
            var previousSession=session.GetValue(null); var previousSnapshot=snapshot.GetValue(null);
            var previousSample=sample.GetValue(null); bool previousWorld=Main.WorldAvailable;
            var harmony=new Harmony("SEVR.HudLifecycle.Tests");
            var getters=new[] {AccessTools.PropertyGetter(typeof(MySession),"LocalCharacter"),AccessTools.PropertyGetter(typeof(MySession),"ControlledEntity")};
            var deadGetter=AccessTools.PropertyGetter(typeof(MyCharacter),"IsDead");
            try
            {
                session.SetValue(null,FormatterServices.GetUninitializedObject(typeof(MySession)));
                foreach(var getter in getters) harmony.Patch(getter,transpiler:new HarmonyMethod(typeof(HudLifecycleTests),nameof(Getter)));
                harmony.Patch(deadGetter,prefix:new HarmonyMethod(typeof(HudLifecycleTests),nameof(Dead)));
                Main.WorldAvailable=true;
                var update=AccessTools.Method(typeof(EssentialHud),"UpdateCore");
                for(int i=0;i<3;i++)
                {
                    character=i==0 ? null:(MyCharacter)FormatterServices.GetUninitializedObject(typeof(MyCharacter));
                    controlled=i==2 ? character:null; dead=i==2;
                    snapshot.SetValue(null,FormatterServices.GetUninitializedObject(snapshot.FieldType));
                    sample.SetValue(null,DateTime.MaxValue);
                    update.Invoke(null,null);
                    if(snapshot.GetValue(null)!=null) throw new Exception("Respawn retained the previous HUD snapshot");
                }
                var armsOwner=AccessTools.Field(typeof(TrackedArms),"owner");
                var fitOwner=AccessTools.Field(typeof(BodyFit),"owner");
                var previousArmsOwner=armsOwner.GetValue(null); var previousFitOwner=fitOwner.GetValue(null);
                try
                {
                    armsOwner.SetValue(null,null); fitOwner.SetValue(null,null);
                    TrackedArms.Restore(null);
                    BodyFit.Restore(null);
                }
                finally { armsOwner.SetValue(null,previousArmsOwner); fitOwner.SetValue(null,previousFitOwner); }
                if(Main.CanResumeMenu(true,false,false,false,true,0) ||
                    Main.CanResumeMenu(true,true,true,true,true,0) ||
                    Main.CanResumeMenu(true,true,true,false,false,0) ||
                    Main.CanResumeMenu(true,true,true,false,true,2) ||
                    !Main.CanResumeMenu(true,true,true,false,true,0))
                    throw new Exception("VR menu recovery ignored respawn readiness or retry limit");
                var recovered=new DateTime(2026,10,17,12,0,0,DateTimeKind.Utc);
                if(Main.RecoveryAttempts(2,recovered,recovered.AddMinutes(4.9))!=2 || Main.RecoveryAttempts(2,recovered,recovered+Main.RecoveryForgiven)!=0)
                    throw new Exception("VR menu recovery attempts did not reset after healthy play");
                ref int hudState=ref AccessTools.StaticFieldRefAccess<int>(typeof(MyHud),"m_hudState");
                int previousHudState=hudState; bool previousMinimal=MyHud.MinimalHud,previousCutscene=MyHud.CutsceneHud;
                try
                {
                    foreach(var (state,minimal,cutscene,suppressed) in new[] { (0,true,false,false),(1,false,false,false),(1,true,false,true),(2,false,true,true),(0,true,true,true) })
                    {
                        hudState=state; MyHud.MinimalHud=minimal; MyHud.CutsceneHud=cutscene;
                        if(HelmetHud.NativeSuppressed!=suppressed) throw new Exception("VR HUD suppression wrong for native state "+state+"/"+minimal+"/"+cutscene);
                    }
                }
                finally { hudState=previousHudState; MyHud.MinimalHud=previousMinimal; MyHud.CutsceneHud=previousCutscene; }
                if(!WorldMarkers.CollectorsResolved || AccessTools.Method(typeof(MyGuiScreenHudSpace),"AsyncUpdate")==null || AccessTools.Field(typeof(MyGuiScreenHudSpace),"m_markerRender")==null)
                    throw new Exception("Native hidden-HUD marker collectors changed");
                if(WorldMarkers.HudTexts?.FieldType!=typeof(VRage.Generics.MyObjectsPoolSimple<MyHudText>))
                    throw new Exception("Native HUD text pool changed; hidden-HUD marker names would not be cleared");
            }
            finally
            {
                foreach(var getter in getters) harmony.Unpatch(getter,AccessTools.Method(typeof(HudLifecycleTests),nameof(Getter)));
                harmony.Unpatch(deadGetter,AccessTools.Method(typeof(HudLifecycleTests),nameof(Dead)));
                session.SetValue(null,previousSession); snapshot.SetValue(null,previousSnapshot); sample.SetValue(null,previousSample);
                Main.WorldAvailable=previousWorld; character=null; controlled=null; dead=false;
            }
            log("PASS respawn lifecycle: missing character, missing control and death clear stale HUD; empty arm/body owners restore safely; Tab-hidden native HUD keeps VR markers while cutscenes and scenario HUDs hide them; installed hidden-HUD marker capture with its names cleared, collectors");
        }
    }
}
#endif
