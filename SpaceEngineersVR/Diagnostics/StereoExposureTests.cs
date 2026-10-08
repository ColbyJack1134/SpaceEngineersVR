using System;
using System.Linq;
using HarmonyLib;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.Mathematics.Interop;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Wrappers;
using Buffer = SharpDX.Direct3D11.Buffer;
using Device = SharpDX.Direct3D11.Device;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class StereoExposureTests
    {
        public static void Run(Action<string> log)
        {
            using (var device = new Device(DriverType.Warp))
            using (var result = new Buffer(device, new BufferDescription(64 * 4, ResourceUsage.Default,
                BindFlags.UnorderedAccess, CpuAccessFlags.None, ResourceOptionFlags.BufferStructured, 4)))
            using (var output = new UnorderedAccessView(device, result))
            {
                try
                {
                    StereoExposure.Create(device);
                    var left = new uint[64]; var right = new uint[64];
                    left[4] = 100; right[54] = 30000;
                    var merged = Merge(device, output, result, left, right);
                    Require(merged[4] == 524288 && merged[54] == 524288 && merged.Sum(v => (long)v) == 1048576,
                        "Eye weighting depends on visible pixel count");
                    Require(merged.SequenceEqual(Merge(device, output, result, right, left)), "Swapping eyes changed exposure");
                    left[4] = 25; left[18] = 75;
                    merged = Merge(device, output, result, left, right);
                    Require(merged[4] == 131072 && merged[18] == 393216 && merged[54] == 524288, "Histogram shape was lost");
                    Array.Clear(right, 0, right.Length);
                    merged = Merge(device, output, result, left, right);
                    Require(merged[4] == 262144 && merged[18] == 786432, "An empty eye darkened the other eye's histogram");
                    Array.Clear(left, 0, left.Length);
                    Require(Merge(device, output, result, left, right).All(v => v == 0), "Empty histograms retained old brightness");
                    using (var sentinel = device.ImmediateContext.ComputeShader.Get())
                        Require(sentinel == null, "Histogram merge changed the engine's compute state");
                }
                finally { StereoExposure.Reset(); }
            }
            log("PASS stereo exposure GPU: equal eye weighting, eye-order invariance, distribution retention, empty-eye handling and context restoration");
        }

        private static uint[] Merge(Device device, UnorderedAccessView output, Buffer result, uint[] left, uint[] right)
        {
            StereoExposure.SetHistogram(device.ImmediateContext, 0, left);
            StereoExposure.SetHistogram(device.ImmediateContext, 1, right);
            StereoExposure.Merge(device.ImmediateContext, output);
            return HiddenAreaTests.Read<uint>(device, result);
        }

        public static void RunNative(BorrowedRtvTexture target, Action<string> log)
        {
            var adaptation = AccessTools.TypeByName("VRageRender.MyEyeAdaptation");
            var history = (Array)AccessTools.Field(adaptation, "m_autoExposure").GetValue(null);
            var getter = AccessTools.Method(adaptation, "GetExposure");
            var active = AccessTools.Field(typeof(StereoRenderState), "<Active>k__BackingField");
            var renderer = AccessTools.TypeByName("VRageRender.MyRender11");
            var settingsField = AccessTools.Field(renderer, "Postprocess");
            var settings = settingsField.GetValue(null);
            var common = AccessTools.TypeByName("VRageRender.MyCommon");
            var timestep = AccessTools.Field(common, "m_lastFrameTimeDelta");
            float previousTimestep = (float)timestep.GetValue(null);
            bool wasActive = StereoRenderState.Active;
            int previousView = StereoRenderState.View;
            var config = SpaceEngineersVR.Plugin.Common.Config;
            bool hiddenArea = config.HiddenAreaMask, stableShadows = config.StableShadows;
            using (var isolated = new RemoteExposure())
            {
                try
                {
                    var testSettings = settingsField.GetValue(null);
                    AccessTools.Field(testSettings.GetType(), "EnableEyeAdaptation").SetValue(testSettings, true);
                    var dataField = AccessTools.Field(testSettings.GetType(), "Data");
                    var data = dataField.GetValue(testSettings);
                    AccessTools.Field(data.GetType(), "EyeAdaptationSpeedUp").SetValue(data, 1f);
                    AccessTools.Field(data.GetType(), "EyeAdaptationSpeedDown").SetValue(data, 1f);
                    dataField.SetValue(testSettings, data);
                    settingsField.SetValue(null, testSettings);
                    config.HiddenAreaMask = config.StableShadows = false;
                    timestep.SetValue(null, .1f);
                    var context = MyRender11.DeviceInstance.ImmediateContext;
                    var initial = (Texture2D)CockpitRender.Member(history.GetValue(0), "Resource");
                    context.UpdateSubresource(new[] { new RawVector2(0, 0) }, initial);
                    active.SetValue(null, true);
                    StereoExposure.Begin();
                    StereoRenderState.View = 0;
                    Draw(target);
                    var left = getter.Invoke(null, null);
                    Require(Read(left).SequenceEqual(new[] { 0f, 0f }), "Left eye changed the shared exposure");
                    Require(Read(history.GetValue(0)).SequenceEqual(new[] { 0f, 0f }), "Left eye advanced native adaptation");
                    StereoRenderState.View = 1;
                    Draw(target);
                    var right = getter.Invoke(null, null);
                    Require(ReferenceEquals(left, right) && Read(right).SequenceEqual(new[] { 0f, 0f }), "Eyes used different exposure textures or values");
                    var next = Read(history.GetValue(0));
                    Require(next.All(v => !float.IsNaN(v) && !float.IsInfinity(v)) && Math.Abs(next[0]) > .0001,
                        "Combined native adaptation did not produce a finite target");
                    Require(Math.Abs(next[1] - next[0] * (1 - Math.Pow(2, -.1))) < .0001,
                        "Adaptation did not advance exactly one timestep: target=" + next[0] + "; actual=" + next[1]);
                    StereoRenderState.View = -1;
                    Draw(target);
                    Require(Read(history.GetValue(0)).SequenceEqual(next), "Diagnostic desktop advanced physical exposure");
                    StereoExposure.End();
                    Require(ReferenceEquals(getter.Invoke(null, null), history.GetValue(0)), "Native exposure was not restored outside stereo");
                    log("PASS native stereo exposure: identical eye texture/pixels, left history unchanged, combined native reduction once per timestep, desktop isolation and scope restoration");
                }
                finally
                {
                    StereoExposure.Reset();
                    active.SetValue(null, wasActive);
                    StereoRenderState.View = previousView;
                    settingsField.SetValue(null, settings);
                    config.HiddenAreaMask = hiddenArea; config.StableShadows = stableShadows;
                    timestep.SetValue(null, previousTimestep);
                }
            }
        }

        private static float[] Read(object exposure) => HiddenAreaTests.Read<float>(MyRender11.DeviceInstance,
            (Texture2D)CockpitRender.Member(exposure, "Resource"));

        private static void Draw(BorrowedRtvTexture target)
        {
            object ambient = null;
            try { MyRender11.DrawGameScene(target, out ambient); }
            finally { if (ambient != null) new BorrowedRtvTexture(ambient).Release(); }
        }

        private static void Require(bool value, string message)
        { if (!value) throw new InvalidOperationException(message); }
    }
}
