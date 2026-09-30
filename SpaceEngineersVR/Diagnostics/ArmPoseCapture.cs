using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Sandbox.Game.Entities.Character;
using SpaceEngineersVR.Player;
using VRage.FileSystem;
using VRageMath;
using VRageRender.Animations;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class ArmPoseCapture
    {
        private static DateTime start,deadline;
        private static int frames;
        private static readonly List<string> rows=new List<string>();
        internal static void Request()
        {
            rows.Clear(); frames=0; start=DateTime.UtcNow.AddSeconds(3); deadline=start.AddSeconds(3);
            rows.Add("time,hand,frame,x,y,z,right_x,right_y,right_z,up_x,up_y,up_z,back_x,back_y,back_z");
            EssentialHud.Notify("Arm capture starts in 3 seconds; hold the pose.");
        }
        internal static void Record(MyCharacter character,Controller hand,MyCharacterBone upper,MyCharacterBone lower,MyCharacterBone palm)
        {
            if(rows.Count==0 || DateTime.UtcNow<start) return;
            string side=hand==Player.Player.HandL ? "left" : "right";
            void Add(string name,MatrixD m)
            {
                var numbers=new[] {m.M41,m.M42,m.M43,m.M11,m.M12,m.M13,m.M21,m.M22,m.M23,m.M31,m.M32,m.M33};
                rows.Add(DateTime.UtcNow.ToString("O")+","+side+","+name+","+
                    string.Join(",",Array.ConvertAll(numbers,n=>n.ToString("R",CultureInfo.InvariantCulture))));
            }
            Add("shoulder",upper.AbsoluteTransform); Add("elbow",lower.AbsoluteTransform); Add("palm",palm.AbsoluteTransform);
            var wrist=character.AnimationController.FindBone(side=="left" ? "SE_RigLForearm2" : "SE_RigRForearm2",out _);
            if(wrist!=null) Add("forearm2",wrist.AbsoluteTransform);
            Add("controller-grip",SpatialUi.DeviceWorld(hand.GripTracking)*character.PositionComp.WorldMatrixNormalizedInv);
            if(TrackedArms.TryDesiredPalm(character,hand,out var desired)) Add("desired-palm",desired*character.PositionComp.WorldMatrixNormalizedInv);
            if(++frames<1000 && DateTime.UtcNow<deadline) return;
            try
            {
                File.WriteAllLines(Path.Combine(MyFileSystem.UserDataPath,"SEVR-arm-poses.csv"),rows);
                EssentialHud.Notify("Arm pose capture saved.");
            }
            catch(Exception ex) { Plugin.Logger.Warning(ex,"Arm pose capture could not be written"); }
            finally { rows.Clear(); }
        }
    }
}
