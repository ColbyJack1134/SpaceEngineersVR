using SpaceEngineersVR.Config;
using SpaceEngineersVR.Plugin;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal static class GripCalibration
    {
        private const int CountdownTicks=300,SampleTicks=30;
        private const float MaxOffset=.1f;
        private static int ticks;
        private static readonly Quaternion[] sums=new Quaternion[2];
        private static readonly Vector3[] positions=new Vector3[2];
        private static int samples;
        private static Matrix[] targets;
        public static bool Active => ticks>0;
        public static string Status { get; private set; }="";

        public static void Start()
        {
            ticks=CountdownTicks+SampleTicks; samples=0; sums[0]=sums[1]=default(Quaternion); positions[0]=positions[1]=Vector3.Zero;
            targets=Targets(Player.Headset.pose.deviceToAbsolute.matrix);
        }
        public static void Cancel() { ticks=0; Status="Alignment cancelled."; }
        public static void Reset()
        {
            Cancel();
            foreach(var hand in new[] { Player.HandL,Player.HandR }) { Alignment.Set(hand.CalibrationKey,null); hand.ReloadCalibration(); }
            Status="Hand alignment reset.";
        }

        // Grip poses that put the posed palms level and straight ahead of the head, at a comfortable reach.
        private static Matrix[] Targets(Matrix head)
        {
            Vector3 forward=head.Forward; forward.Y=0;
            if(forward.LengthSquared()<1e-4f) return null;
            forward.Normalize(); var right=Vector3.Cross(forward,Vector3.Up);
            var grip=Matrix.Transpose(ArmMath.HandInGrip)*Matrix.CreateWorld(Vector3.Zero,forward,Vector3.Up);
            var result=new Matrix[2];
            for(int i=0;i<2;i++)
            {
                result[i]=grip;
                result[i].Translation=head.Translation+forward*.4f+right*(i==0 ? -.17f:.17f)-Vector3.Up*.3f;
            }
            return result;
        }
        internal static bool TryTarget(int side,out Matrix grip)
        {
            var current=targets; grip=Matrix.Identity;
            if(ticks<=0 || current==null) return false;
            grip=current[side]; return true;
        }

        public static void Update()
        {
            if(ticks<=0) return;
            ticks--;
            if(!Player.Headset.pose.isTracked || !Player.HandL.pose.isTracked || !Player.HandR.pose.isTracked)
            { ticks=0; Status="Alignment failed. Keep the headset and both controllers tracked."; return; }
            if(targets==null) { ticks=0; Status="Alignment failed. Look straight ahead and try again."; return; }
            if(ticks>=SampleTicks)
            {
                int seconds=(ticks-SampleTicks+59)/60;
                Status="Place your hands in the blue hands and hold still: "+seconds;
                if((ticks-SampleTicks)%60==0) Pulse(.02f);
                return;
            }
            Sample(0,Player.HandL); Sample(1,Player.HandR); samples++;
            if(ticks==0) Finish();
        }
        private static void Sample(int index,Controller hand)
        {
            var grip=hand.UncalibratedGripTracking;
            var q=Quaternion.CreateFromRotationMatrix(grip.GetOrientation());
            if(samples>0 && Quaternion.Dot(q,sums[index])<0) q=-q;
            sums[index]+=q; positions[index]+=grip.Translation;
        }
        private static void Finish()
        {
            var hands=new[] { Player.HandL,Player.HandR };
            for(int i=0;i<2;i++)
            {
                var grip=Matrix.CreateFromQuaternion(Quaternion.Normalize(sums[i]));
                grip.Translation=positions[i]/samples;
                var setting=Solve(hands[i].CalibrationKey,targets[i],grip);
                Alignment.Set(setting.Key,setting); hands[i].ReloadCalibration();
                Logger.Info("Grip alignment "+setting.Key+" ("+hands[i].GripSource+"): Pitch="+setting.Pitch.ToString("0.0")+
                    " Yaw="+setting.Yaw.ToString("0.0")+" Roll="+setting.Roll.ToString("0.0")+
                    " X="+setting.X.ToString("0.000")+" Y="+setting.Y.ToString("0.000")+" Z="+setting.Z.ToString("0.000"));
            }
            Status="Hands aligned.";
            Pulse(.08f);
        }
        // correction * grip = target grip, so the posed palm matches the shown target hand.
        // The translation is in the uncalibrated grip frame and moves its origin onto the target's.
        internal static AnchorOffsetSetting Solve(string key,Matrix target,Matrix grip)
        {
            var orientation=Matrix.Transpose(grip.GetOrientation());
            var offset=Vector3.TransformNormal(target.Translation-grip.Translation,orientation);
            if(offset.Length()>MaxOffset) offset*=MaxOffset/offset.Length();
            var setting=AnchorOffsetSetting.FromRotation(key,target.GetOrientation()*orientation);
            setting.X=offset.X; setting.Y=offset.Y; setting.Z=offset.Z;
            return setting;
        }
        private static void Pulse(float seconds)
        {
            Player.HandL.Vibrate(0,seconds,150,.5f); Player.HandR.Vibrate(0,seconds,150,.5f);
        }
    }
}
