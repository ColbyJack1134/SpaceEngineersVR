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
            internal bool Contains(float distance,float radius) => distance<radius*(inside ? 1.15f:1);
            public bool Sample(bool available,float distance,float radius,bool suppressed=false)
            {
                if(!available) { inside=false; return false; }
                if(inside) { if(distance>=radius*1.15f) inside=false; return false; }
                if(distance>=radius || float.IsNaN(distance)) return false;
                inside=true;
                return !suppressed;
            }
        }
        internal sealed class ValuePulse
        {
            private float anchor;
            private DateTime next;
            internal void Reset(float value) { anchor=value; next=DateTime.MinValue; }
            internal bool Sample(float value,DateTime now)
            {
                if(float.IsNaN(value) || float.IsInfinity(value)) return false;
                float change=Math.Abs(value-anchor);
                if(now<next || change<.02f && !(change>.0001f && (value<=0 || value>=1))) return false;
                anchor=value; next=now.AddMilliseconds(50); return true;
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
        internal sealed class StickPulse
        {
            private bool held,tiltOutside,twistOutside;
            private int limits;
            private DateTime next;
            private static bool Crossed(ref bool outside,float amount)
            {
                bool was=outside;
                // Inputs already passed through the deadzone. A small exit margin prevents boundary chatter.
                if(amount>.025f) outside=true;
                else if(amount==0) outside=false;
                return was!=outside;
            }
            internal int Sample(bool captured,Vector3 axes,DateTime now)
            {
                if(!captured || !axes.IsValid()) { held=tiltOutside=twistOutside=false; limits=0; return 0; }
                float tilt=new Vector2(axes.X,axes.Z).Length(),twist=Math.Abs(axes.Y);
                if(!held)
                {
                    held=true; tiltOutside=tilt>.025f; twistOutside=twist>.025f;
                    limits=0; next=now.AddMilliseconds(80); return 0;
                }
                bool boundary=Crossed(ref tiltOutside,tilt);
                boundary=Crossed(ref twistOutside,twist) || boundary;
                int current=limits;
                for(int i=0;i<3;i++)
                {
                    float a=Math.Abs(i==0 ? axes.X:i==1 ? axes.Y:axes.Z);
                    if(a>=.98f) current|=1<<i;
                    else if(a<.90f) current&=~(1<<i);
                }
                bool limit=(current & ~limits)!=0;
                limits=current;
                if(now<next || !limit && !boundary) return 0;
                next=now.AddMilliseconds(130); return limit ? 2:1;
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
        public static void Activate(Controller hand,float amplitude=.25f,float duration=.024f) => hand.Vibrate(0,duration,105,amplitude);
        public static void Click(Controller hand,bool cover=false,float amplitude=.25f,float duration=.024f)
        {
            Activate(hand,amplitude,duration);
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
