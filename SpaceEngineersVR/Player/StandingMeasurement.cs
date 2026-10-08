using System;
using System.Collections.Generic;
using SpaceEngineersVR.Config;

namespace SpaceEngineersVR.Player
{
    internal sealed class StandingMeasurement
    {
        private readonly List<float> samples=new List<float>(300);
        internal void Clear() => samples.Clear();
        internal void Sample(float height)
        {
            if(!float.IsNaN(height) && !float.IsInfinity(height) && height>.3f && height<2.5f) samples.Add(height);
        }
        internal bool TryGet(out float height)
        {
            height=0;
            if(samples.Count<120) return false;
            var ordered=samples.ToArray(); Array.Sort(ordered);
            if(ordered[(ordered.Length-1)*9/10]-ordered[(ordered.Length-1)/10]>.04f) return false;
            height=(ordered[(ordered.Length-1)/2]+ordered[ordered.Length/2])*.5f;
            return true;
        }
        internal bool TryApply(PluginConfig config,bool tracked,out float height,bool estimateBodyHeight=false)
        {
            height=0;
            if(!tracked || !TryGet(out height)) return false;
            config.MeasuredEyeHeight=height;
            if(estimateBodyHeight) config.PlayerHeight=VRageMath.MathHelper.Clamp(height*(1.8f/1.69f),1f,2.4f);
            config.SeatedPlay=false;
            config.BodyCalibrated=true;
            return true;
        }
    }
}
