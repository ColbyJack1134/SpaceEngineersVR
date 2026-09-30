using HarmonyLib;
using Sandbox.Game.Entities.Character;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Plugin;
using VRage.Game.Entity;

namespace SpaceEngineersVR.Patches
{
    [HarmonyPatch(typeof(MyCharacter), "UpdateNearFlag")]
    internal static class CharacterDepthPatch
    {
        private static readonly AccessTools.FieldRef<MyCharacter,MyEntity> leftItem=AccessTools.FieldRefAccess<MyCharacter,MyEntity>("m_leftHandItem");
        private static void Postfix(MyCharacter __instance)
        {
            if(!Main.VrActive || !CameraRig.Owns(__instance)) return;
            // Body and held items must all use world depth; vanilla biases both
            // hands' items forward again on each UpdateNearFlag call.
            __instance.Render.NearFlag=false;
            if(__instance.CurrentWeapon is MyEntity weapon) weapon.Render.NearFlag=false;
            var item=leftItem(__instance);
            if(item!=null) item.Render.NearFlag=false;
        }
    }
}
