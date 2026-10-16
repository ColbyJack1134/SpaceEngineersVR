using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Sandbox.Definitions;
using SpaceEngineersVR.Plugin;
using VRageMath;

namespace SpaceEngineersVR.Patches
{
    // Native trails are a few millimetres thick and scaled by the desktop zoom level, which VR never changes.
    [HarmonyPatch]
    internal static class TracerPatch
    {
        internal const float ThicknessScale=3,LengthScale=2;
        private static MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName("Sandbox.Game.Weapons.MyProjectile"),"Draw");
        internal static float Thickness(float value) => Main.VrActive ? value*ThicknessScale:value;
        internal static float Length(float value) => Main.VrActive ? value*LengthScale:value;
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var list=instructions.ToList();
            var scale=AccessTools.Field(typeof(MyProjectileAmmoDefinition),nameof(MyProjectileAmmoDefinition.ProjectileTrailScale));
            var lerp=AccessTools.Method(typeof(MathHelper),nameof(MathHelper.Lerp),new[] {typeof(float),typeof(float),typeof(float)});
            int length=list.FindIndex(i=>i.LoadsField(scale));
            int width=list.FindIndex(i=>i.Calls(lerp));
            if(length<0 || width<length) throw new InvalidOperationException("Native projectile trail drawing changed");
            list.Insert(width+1,CodeInstruction.Call(typeof(TracerPatch),nameof(Thickness)));
            list.Insert(length+1,CodeInstruction.Call(typeof(TracerPatch),nameof(Length)));
            return list;
        }
    }
}
