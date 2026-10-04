using System;
using SpaceEngineersVR.Plugin;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Valve.VR;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal sealed class ControllerFingers
    {
        private static readonly uint dataSize=(uint)Marshal.SizeOf(typeof(InputSkeletalActionData_t));
        private readonly float[] curls=new float[5],target=new float[5];
        private ulong handle;
        private long last;
        private int retry;
        private bool? skeletal;
        internal float[] Curls => curls;
        internal void Reset() { handle=0; last=0; retry=0; skeletal=null; Array.Clear(curls,0,5); }
        internal void Update(Controller hand,bool left)
        {
            long now=Stopwatch.GetTimestamp();
            float elapsed=last==0 ? 1:(float)((now-last)/(double)Stopwatch.Frequency); last=now;
            var controls=Controls.Static;
            if(!hand.pose.isTracked || controls==null) { Array.Clear(curls,0,5); return; }
            float trigger=(left ? controls.LeftTriggerPressure:controls.PointerPressure).RawPosition.X;
            float grip=(left ? controls.LeftGripPressure:controls.RightGripPressure).RawPosition.X;
            Fallback(target,trigger,grip);
            bool available=false;
            var input=OpenVR.Input;
            if(input!=null)
            {
                if(handle==0 && retry++%120==0)
                    input.GetActionHandle("/actions/common/in/"+(left ? "LeftHand":"RightHand"),ref handle);
                var active=new InputSkeletalActionData_t();
                var summary=new VRSkeletalSummaryData_t();
                if(handle!=0 && input.GetSkeletalActionData(handle,ref active,dataSize)==EVRInputError.None && active.bActive &&
                    input.GetSkeletalSummaryData(handle,EVRSummaryType.FromAnimation,ref summary)==EVRInputError.None)
                {
                    available=true;
                    target[0]=Clean(summary.flFingerCurl0,target[0]); target[1]=Clean(summary.flFingerCurl1,target[1]);
                    target[2]=Clean(summary.flFingerCurl2,target[2]); target[3]=Clean(summary.flFingerCurl3,target[3]);
                    target[4]=Clean(summary.flFingerCurl4,target[4]);
                }
            }
            if(skeletal!=available)
            { skeletal=available; Logger.Info((left ? "Left":"Right")+" hand fingers: "+(available ? "SteamVR skeleton":"trigger/grip fallback")); }
            Smooth(curls,target,elapsed);
        }
        private static float Clean(float value,float fallback) => float.IsNaN(value) || float.IsInfinity(value) ? fallback:MathHelper.Clamp(value,0,1);
        internal static void Fallback(float[] output,float trigger,float grip)
        {
            trigger=Clean(trigger,0); grip=Clean(grip,0);
            output[0]=grip*.65f; output[1]=trigger;
            output[2]=output[3]=output[4]=grip;
        }
        internal static void Smooth(float[] values,float[] target,float elapsed)
        {
            float blend=1-(float)Math.Exp(-MathHelper.Clamp(elapsed,0,1)/.055f);
            for(int i=0;i<5;i++) values[i]=MathHelper.Lerp(values[i],target[i],blend);
        }
    }
}
