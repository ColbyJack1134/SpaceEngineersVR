using System;

namespace SpaceEngineersVR.Player
{
    // Survives camera detachment for a cockpit, but not world/model/character changes.
    internal sealed class EyeHeightCalibration
    {
        private object owner,definition;
        private double height;
        public void Clear() { owner=definition=null; height=0; }
        private static bool InRange(double value,double low,double high) =>
            !double.IsNaN(value) && !double.IsInfinity(value) && value>=low && value<=high;
        public double Get(object character,object suit,double animated,double bindPose,double collisionHeight)
        {
            if(ReferenceEquals(owner,character) && ReferenceEquals(definition,suit) && height>0) return height;
            double standing=InRange(collisionHeight,0.5,5) ? collisionHeight : 1.8;
            double reference=InRange(bindPose,standing*0.55,standing*1.25) ? bindPose : standing*0.9;
            // Sitting/ejection/uninitialized bones are not a standing-eye calibration.
            // Use the animated height only when it agrees with the suit's rest pose.
            height=InRange(animated,reference*0.85,reference*1.15) ? animated : reference;
            owner=character; definition=suit;
            return height;
        }
    }
}
