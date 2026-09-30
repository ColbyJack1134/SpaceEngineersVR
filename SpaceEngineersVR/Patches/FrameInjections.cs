using System;
using HarmonyLib;
using SpaceEngineersVR.Player.Components;
using SpaceEngineersVR.Plugin;
using Valve.VR;

namespace SpaceEngineersVR.Patches
{
    public static class FrameInjections
    {
        private static bool posesAcquired;
        private static bool drewStereo;
        private static bool reportedCameraFrame;
        public static void Install(Harmony harmony)
        {
            Type type = AccessTools.TypeByName("VRageRender.MyRender11");
            harmony.Patch(AccessTools.Method(type,"SetupCameraMatrices"), new HarmonyMethod(typeof(FrameInjections),nameof(CameraFrame)));
            harmony.Patch(AccessTools.Method(type,"DrawScene"), new HarmonyMethod(typeof(FrameInjections),nameof(BeforeScene)));
            harmony.Patch(AccessTools.Method(type,"ConsumeMainSprites"), new HarmonyMethod(typeof(FrameInjections),nameof(BeforeSprites)));
            harmony.Patch(AccessTools.Method(type,"Present"),
                new HarmonyMethod(typeof(FrameInjections),nameof(BeforePresent)), new HarmonyMethod(typeof(FrameInjections),nameof(AfterPresent)));
        }
        private static void CameraFrame(VRageRender.Messages.MyRenderMessageSetCameraViewMatrix message)
        {
            if (!Main.VrActive) return;
            Player.RenderFrameBridge.Consume(message);
            if (!reportedCameraFrame && Player.RenderFrameBridge.Current != null)
            {
                reportedCameraFrame=true;
                Logger.Info("VR camera received with native rendered frame; simulation/render handoff active");
            }
        }
        private static void GetPoses()
        {
            if (posesAcquired) return;
            Player.Player.RenderUpdate();
            posesAcquired = true;
        }
        private static bool BeforeScene()
        {
            if (!Main.VrActive) { Player.EyeResolution.Scene.RestoreNative(); return true; }
            try
            {
                GetPoses();
                if (!Main.MenuOpen || Main.WorldAvailable)
                    drewStereo = Player.Player.Headset.RenderUpdate();
            }
            catch (Exception ex)
            {
                Player.StereoRenderState.CancelFrame(); Player.EyeResolution.Scene.RestoreNative();
                Main.Fail(ex, "Stereo rendering stopped; desktop remains available");
            }
            bool native=!drewStereo || !Main.VrActive || !Player.Player.Headset.MirroredDesktop;
            if(native) Player.EyeResolution.Scene.RestoreNative();
            return native;
        }
        private static void BeforeSprites()
        {
            if (!Main.VrActive || !Main.MenuOpen || !Main.WorldAvailable || !drewStereo) return;
            // Native sprites composite after the scene. Isolate them on a readable panel.
            var source = (SharpDX.Direct3D11.Texture2D)Wrappers.MyRender11.GetBackbuffer().GetResource();
            using (var target = new SharpDX.Direct3D11.RenderTargetView(Wrappers.MyRender11.DeviceInstance, source))
                Wrappers.MyRender11.DeviceInstance.ImmediateContext.ClearRenderTargetView(target,
                    new SharpDX.Mathematics.Interop.RawColor4(0.025f, 0.035f, 0.05f, 1));
        }
        private static void BeforePresent()
        {
            if (!Main.VrActive) return;
            try
            {
                GetPoses();
                Player.NativeSprites.Poll();
                VRGUIManager.Draw();
                Player.EssentialHud.Draw();
                Player.BuildOrientationHud.Draw();
                if (!drewStereo) VRGUIManager.SubmitMenuBackground();
            }
            catch (Exception ex) { Main.Fail(ex,"VR presentation stopped"); }
        }
        private static void AfterPresent()
        {
            if(Main.VrActive && drewStereo) Player.RenderPerformance.Sample();
            Player.StereoRenderState.End();
            if (Main.VrActive) OpenVR.Compositor.PostPresentHandoff();
            posesAcquired = drewStereo = false;
        }
    }
}
