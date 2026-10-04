using System;
using SpaceEngineersVR.Player;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class ControllerFingerTests
    {
        private static void Require(bool value,string message) { if(!value) throw new Exception(message); }
        internal static void Run(Action<string> log)
        {
            var target=new float[5]; var values=new float[5];
            ControllerFingers.Fallback(target,1,0);
            Require(target[1]==1 && target[0]==0 && target[2]==0,"Trigger curls unrelated fingers");
            ControllerFingers.Fallback(target,0,1);
            Require(target[1]==0 && target[0]>.5f && target[2]==1 && target[3]==1 && target[4]==1,"Grip loses independent index");
            ControllerFingers.Smooth(values,target,1f/90);
            Require(values[2]>0 && values[2]<.3f,"Curl smoothing snaps or stalls");
            for(int i=0;i<90;i++) ControllerFingers.Smooth(values,target,1f/90);
            Require(values[2]>.999f,"Curl smoothing does not reach closed pose");
            ControllerFingers.Fallback(target,0,0);
            for(int i=0;i<90;i++) ControllerFingers.Smooth(values,target,1f/90);
            Require(values[2]<.001f,"Released grip does not reopen");
            ControllerFingers.Fallback(target,float.NaN,float.PositiveInfinity);
            Require(target[0]==0 && target[1]==0 && target[4]==0,"Invalid input reaches finger transforms");
            foreach(string side in new[] {"L","R"}) foreach(string finger in new[] {"Thumb","Index","Middle","Ring","Pinky"})
                for(int joint=1;joint<=3;joint++)
                {
                    string name="SE_Rig"+side+"_"+finger+"_"+joint;
                    var open=CockpitHandPose.FreeRotation(name,new float[5]);
                    var closed=CockpitHandPose.FreeRotation(name,new[] {1f,1f,1f,1f,1f});
                    Require(Math.Abs(open.LengthSquared()-1)<.00001 && Math.Abs(closed.LengthSquared()-1)<.00001 &&
                        Math.Abs(Quaternion.Dot(open,closed))<.99f,"Open and closed finger poses are invalid or identical");
                }
            log("PASS controller fingers: independent index/grip fallback, smooth curl/release, invalid input and mirrored open/closed rotations.");
        }
    }
}
