using System;
using Sandbox.Game;
using Sandbox.Game.Entities;
using VRage.Audio;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal static class CockpitFeedback
    {
        private static readonly DateTime[] nextHover=new DateTime[2];
        private static readonly MotionPulse[] motion={new MotionPulse(),new MotionPulse()};
        internal sealed class ProximityPulse
        {
            private bool inside;
            public bool Sample(bool available,float distance,float radius,bool suppressed=false)
            {
                if(!available) { inside=false; return false; }
                if(inside) { if(distance>radius*1.15f) inside=false; return false; }
                if(distance>=radius || float.IsNaN(distance)) return false;
                inside=true;
                return !suppressed;
            }
        }
        internal sealed class MotionPulse
        {
            private Vector3 anchor;
            private bool held;
            private DateTime next;
            public float Sample(bool captured,Vector3 input,DateTime now,bool suppressed=false)
            {
                if(!captured || !held || suppressed)
                { held=captured; anchor=input; next=now.AddMilliseconds(65); return 0; }
                float change=Vector3.Distance(input,anchor);
                if(change<.025f || now<next) return 0;
                anchor=input; next=now.AddMilliseconds(65);
                return MathHelper.Clamp(change*.65f,.025f,.11f);
            }
        }
        private static bool soundFailed;
        private static int Index(Controller hand) => hand==Player.HandL ? 1 : 0;
        public static void Hover(Controller hand)
        {
            int i=Index(hand);
            if(DateTime.UtcNow<nextHover[i]) return;
            hand.Vibrate(0,.012f,125,.12f);
            nextHover[i]=DateTime.UtcNow.AddMilliseconds(100);
        }
        public static void Engage(Controller hand) => hand.Vibrate(0,.018f,110,.18f);
        public static void Activate(Controller hand) => hand.Vibrate(0,.024f,105,.25f);
        public static void Click(Controller hand,bool cover=false)
        {
            Activate(hand);
            if(soundFailed) return;
            try
            {
                if(!cover) MyAudio.Static?.PlaySound(MySoundPair.GetCueId("HudClick"));
                else
                {
                    var voice=MyAudio.Static?.GetSound(MySoundPair.GetCueId("ArcHudItem"));
                    if(voice==null) return;
                    voice.VolumeMultiplier=.2f;
                    voice.FrequencyRatio=1; // Keep the small latch consistent instead of the cue's random pitch.
                    voice.Start(false);
                }
            }
            catch(Exception ex) { soundFailed=true; Plugin.Logger.Warning(ex,"Cockpit click sound unavailable"); }
        }
        public static void Motion(Controller hand,bool held,Vector3 input,bool suppressed)
        {
            float amplitude=motion[Index(hand)].Sample(held,input,DateTime.UtcNow,suppressed);
            if(amplitude>0) hand.Vibrate(0,.012f,85,amplitude);
        }
    }
}
