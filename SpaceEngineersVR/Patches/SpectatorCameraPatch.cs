using HarmonyLib;
using Sandbox.Engine.Utils;
using Sandbox.Game.World;
using Sandbox.Game.SessionComponents;
using Sandbox.ModAPI;
using SpaceEngineersVR.Player;
using VRage.Game.Utils;
using VRageMath;

namespace SpaceEngineersVR.Patches
{
    [HarmonyPatch(typeof(MySessionComponentSpectatorTools),nameof(MySessionComponentSpectatorTools.LockHitEntity))]
    internal static class SpectatorLockPatch
    {
        private static bool Prefix()
        {
            if(!SpectatorView.Active || MyAPIGateway.SpectatorTools?.GetTarget()!=null) return true;
            GridSelection.Arm("Spectator lock");
            return false;
        }
    }
    [HarmonyPatch(typeof(MySessionComponentSpectatorTools),nameof(MySessionComponentSpectatorTools.UpdateAfterSimulation))]
    internal static class SpectatorFollowPatch
    {
        // VR follow uses the same reference and gesture engine as third person; native movement would compete with it.
        private static bool Prefix() => !SpectatorView.Following || SpectatorView.RestoringTrackedView;
    }
    [HarmonyPatch(typeof(MySpectatorCameraController),"VRage.Game.ModAPI.Interfaces.IMyCameraController.ControlCamera")]
    internal static class SpectatorCameraPatch
    {
        private static bool Prefix(MyCamera currentCamera)
        {
            if(!SpectatorView.Active) return true;
            SpectatorView.Publish();
            var frame=SpectatorView.Current;
            if(frame==null) return true;
            CameraRig.ApplyObserverCamera(currentCamera,frame);
            return false;
        }
    }
    [HarmonyPatch(typeof(MySpectatorCameraController),"MoveAndRotate_UserControlled")]
    internal static class SpectatorGravityCameraPatch
    {
        // Zero movement still runs native gravity alignment, which competes with VR orientation.
        private static bool Prefix() => !SpectatorView.Active;
    }
    [HarmonyPatch(typeof(MySessionComponentSpectatorTools),"UpdateLockEntity")]
    internal static class SpectatorTargetPosePatch
    {
        private static MatrixD World(MyCamera camera) => SpectatorView.CapturePose ?? camera.WorldMatrix;
        private static Vector3D Position(MyCamera camera) => (SpectatorView.CapturePose ?? camera.WorldMatrix).Translation;
        private static System.Collections.Generic.IEnumerable<CodeInstruction> Transpiler(System.Collections.Generic.IEnumerable<CodeInstruction> instructions,System.Reflection.Emit.ILGenerator generator)
        {
            var world=AccessTools.Field(typeof(MyCamera),nameof(MyCamera.WorldMatrix));
            var position=AccessTools.PropertyGetter(typeof(MyCamera),nameof(MyCamera.Position));
            bool foundWorld=false,foundPosition=false;
            foreach(var instruction in instructions)
            {
                if((instruction.opcode==System.Reflection.Emit.OpCodes.Ldfld || instruction.opcode==System.Reflection.Emit.OpCodes.Ldflda) && Equals(instruction.operand,world))
                {
                    bool address=instruction.opcode==System.Reflection.Emit.OpCodes.Ldflda;
                    instruction.opcode=System.Reflection.Emit.OpCodes.Call; instruction.operand=AccessTools.Method(typeof(SpectatorTargetPosePatch),nameof(World)); foundWorld=true;
                    yield return instruction;
                    if(address)
                    {
                        // The native struct method takes a field address; keep it on a local snapshot.
                        var snapshot=generator.DeclareLocal(typeof(MatrixD));
                        yield return new CodeInstruction(System.Reflection.Emit.OpCodes.Stloc,snapshot);
                        yield return new CodeInstruction(System.Reflection.Emit.OpCodes.Ldloca,snapshot);
                    }
                    continue;
                }
                else if(instruction.Calls(position))
                { instruction.opcode=System.Reflection.Emit.OpCodes.Call; instruction.operand=AccessTools.Method(typeof(SpectatorTargetPosePatch),nameof(Position)); foundPosition=true; }
                yield return instruction;
            }
            if(!foundWorld || !foundPosition) throw new System.InvalidOperationException("Native spectator target pose changed");
        }
    }
}
