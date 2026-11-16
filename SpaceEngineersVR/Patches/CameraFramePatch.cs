using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Plugin;
using VRageRender;
using VRageRender.Messages;

namespace SpaceEngineersVR.Patches
{
    [HarmonyPatch(typeof(MyRenderProxy),"EnqueueMessage")]
    internal static class CameraFramePatch
    {
        private static void Prefix(MyRenderMessageBase message)
        {
            if(SmoothFramesBridge.Ready) return;
            if(message is MyRenderMessageSetCharacterTransforms bones) FitBody(bones);
            if(Main.VrActive && message is MyRenderMessageSetCameraViewMatrix)
                RenderFrameBridge.Capture(message,CameraRig.Current,ShipCrosshair.Capture());
        }
        internal static void FitBody(MyRenderMessageSetCharacterTransforms bones)
        {
            try { BodyFit.Render(bones); }
            catch(Exception ex) when(SmoothFramesBridge.Ready || SmoothFramesBridge.Failure!=null) { SmoothFramesBridge.Fail(ex); }
        }
    }
    [HarmonyPatch(typeof(MyRenderProxy),nameof(MyRenderProxy.AfterUpdate))]
    internal static class CameraFrameCommitPatch
    {
        private static void Prefix() { if(!SmoothFramesBridge.Ready) RenderFrameBridge.Commit(); }
    }
    [HarmonyPatchCategory("SmoothFramesBridge")]
    [HarmonyPatch(typeof(MyRenderProxy),nameof(MyRenderProxy.SetCharacterTransforms))]
    internal static class SmoothFramesBonesPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var enqueue=AccessTools.Method(typeof(MyRenderProxy),"EnqueueMessage");
            int calls=0;
            foreach(var instruction in instructions)
            {
                if(instruction.Calls(enqueue))
                {
                    calls++;
                    yield return new CodeInstruction(OpCodes.Dup).MoveLabelsFrom(instruction);
                    yield return new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(SmoothFramesBonesPatch),nameof(Capture)));
                }
                yield return instruction;
            }
            if(calls!=1) throw new InvalidOperationException("Native bone enqueue call changed: "+calls);
        }
        private static void Capture(MyRenderMessageBase message)
        {
            if(SmoothFramesBridge.Ready && message is MyRenderMessageSetCharacterTransforms bones) CameraFramePatch.FitBody(bones);
        }
    }

    [HarmonyPatchCategory("SmoothFramesBridge")]
    [HarmonyPatch(typeof(Sandbox.MySandboxGame),"AfterDraw")]
    internal static class SmoothFramesCommitPatch
    {
        private static readonly FieldInfo renderer=AccessTools.Field(typeof(MyRenderProxy),"m_render");
        private static void Prefix()
        {
            if(!SmoothFramesBridge.Ready) return;
            try
            {
                var shared=((IMyRender)renderer.GetValue(null))?.SharedData;
                if(Main.VrActive && shared!=null) RenderFrameBridge.CaptureBatch(shared.CurrentUpdateFrame.RenderInput,CameraRig.Current);
                SmoothFramesBridge.CaptureLate();
                RenderFrameBridge.Commit();
            }
            catch(Exception ex) { SmoothFramesBridge.Fail(ex); }
        }
    }
}
