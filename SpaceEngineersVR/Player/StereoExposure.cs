using System;
using System.Reflection;
using HarmonyLib;
using SharpDX.D3DCompiler;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SpaceEngineersVR.Wrappers;
using Buffer = SharpDX.Direct3D11.Buffer;
using Device = SharpDX.Direct3D11.Device;

namespace SpaceEngineersVR.Player
{
    internal static class StereoExposure
    {
        private const string MergeShader = @"
StructuredBuffer<uint> Left : register(t0);
StructuredBuffer<uint> Right : register(t1);
RWStructuredBuffer<uint> Combined : register(u0);
groupshared uint leftTotal, rightTotal;
[numthreads(64,1,1)]
void CS(uint index : SV_GroupIndex)
{
    if (index == 0)
    {
        leftTotal = 0; rightTotal = 0;
        for (uint i = 0; i < 64; i++) { leftTotal += Left[i]; rightTotal += Right[i]; }
    }
    GroupMemoryBarrierWithGroupSync();
    float value = (leftTotal > 0 ? (float)Left[index] / leftTotal : 0)
                + (rightTotal > 0 ? (float)Right[index] / rightTotal : 0);
    uint eyes = (leftTotal > 0 ? 1 : 0) + (rightTotal > 0 ? 1 : 0);
    Combined[index] = (uint)round(value * 1048576 / max(eyes, 1));
}";
        private static readonly FieldInfo history = AccessTools.Field(AccessTools.TypeByName("VRageRender.MyEyeAdaptation"), "m_autoExposure");
        private static Device device;
        private static DeviceContext commands;
        private static ComputeShader merge;
        private static readonly Buffer[] histograms = new Buffer[2];
        private static readonly ShaderResourceView[] views = new ShaderResourceView[2];
        private static BorrowedRtvTexture exposure;
        private static bool leftCaptured;

        internal static bool DiagnosticDesktop => exposure != null && StereoRenderState.Active && StereoRenderState.View == -1;

        internal static void Begin()
        {
            End();
            var current = ((Array)history.GetValue(null)).GetValue(0);
            var source = (Texture2D)CockpitRender.Member(current, "Resource");
            exposure = MyManagers.RwTexturesPool.BorrowRtv("SEVR.StereoExposure", 1, 1, Format.R32G32_Float);
            MyRender11.DeviceInstance.ImmediateContext.CopyResource(source, exposure.GetResource());
        }

        // Keep the native histogram pass and reduce its combined result only in the right eye.
        internal static bool Collect(object histogram)
        {
            int eye = StereoRenderState.Active ? StereoRenderState.View : -1;
            if (exposure == null || eye < 0 || eye > 1) return false;
            var target = MyRender11.DeviceInstance;
            if (device == null || device.NativePointer != target.NativePointer) Create(target);
            var source = (Buffer)CockpitRender.Member(histogram, "Resource");
            target.ImmediateContext.CopyResource(source, histograms[eye]);
            if (eye == 0)
            {
                leftCaptured = true;
                return true;
            }
            if (!leftCaptured) return false;
            var output = (UnorderedAccessView)CockpitRender.Member(histogram, "Uav");
            Merge(target.ImmediateContext, output);
            if (StereoRenderState.Tracing) StereoRenderState.Record("exposure_combined");
            return false;
        }

        internal static object Select(object native)
        {
            int eye = StereoRenderState.Active ? StereoRenderState.View : -1;
            return leftCaptured && eye >= 0 && eye <= 1 ? exposure.Instance : native;
        }

        internal static void Create(Device target)
        {
            DisposeResources();
            device = target;
            commands = new DeviceContext(target);
            using (var shader = ShaderBytecode.Compile(MergeShader, "CS", "cs_5_0")) merge = new ComputeShader(target, shader);
            for (int i = 0; i < 2; i++)
            {
                histograms[i] = new Buffer(target, new BufferDescription(64 * 4, ResourceUsage.Default,
                    BindFlags.ShaderResource, CpuAccessFlags.None, ResourceOptionFlags.BufferStructured, 4));
                views[i] = new ShaderResourceView(target, histograms[i]);
            }
        }

        internal static void Merge(DeviceContext immediate, UnorderedAccessView output)
        {
            commands.ClearState();
            commands.ComputeShader.Set(merge);
            commands.ComputeShader.SetShaderResources(0, views);
            commands.ComputeShader.SetUnorderedAccessView(0, output);
            commands.Dispatch(1, 1, 1);
            // Replay with restoration so the engine's cached context state remains valid.
            using (var list = commands.FinishCommandList(false)) immediate.ExecuteCommandList(list, true);
        }

        internal static void SetHistogram(DeviceContext context, int eye, uint[] values) => context.UpdateSubresource(values, histograms[eye]);

        internal static void End()
        {
            leftCaptured = false;
            exposure?.Release();
            exposure = null;
        }

        internal static void Reset()
        {
            End();
            DisposeResources();
        }

        private static void DisposeResources()
        {
            for (int i = 0; i < 2; i++)
            {
                views[i]?.Dispose(); views[i] = null;
                histograms[i]?.Dispose(); histograms[i] = null;
            }
            merge?.Dispose(); merge = null;
            commands?.Dispose(); commands = null;
            device = null;
        }
    }
}
