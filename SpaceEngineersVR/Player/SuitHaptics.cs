using Sandbox.Game.Entities.Character;
using Sandbox.Game.World;
using SpaceEngineersVR.Plugin;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    // Pulses in both hands for suit events: magnetic boots lock and release, jetpack ignition and hard landings.
    internal static class SuitHaptics
    {
        // Landing speed along gravity: no pulse below a jump landing, strongest from about a three-storey fall.
        private const float LandingMin=6,LandingFull=15;
        private static MyCharacter owner;
        private static bool locked,jetpack,falling;
        private static float fallSpeed;
        public static void Update()
        {
            var character=MySession.Static?.LocalCharacter;
            bool active=Main.VrActive && character!=null && !character.IsDead;
            bool boots=active && character.IsMagneticBootsActive,flying=active && character.JetpackRunning;
            if(!ReferenceEquals(character,owner) || character?.IsSitting==true)
            { owner=character; locked=boots; jetpack=flying; falling=false; return; }
            if(boots!=locked)
            {
                locked=boots;
                if(boots) Pulse(.05f,80,.2f); else Pulse(.03f,110,.1f);
            }
            if(flying!=jetpack)
            {
                jetpack=flying;
                if(flying) Pulse(.04f,70,.18f);
            }
            Landing(character,active && character.IsFalling,flying);
        }
        // The peak fall speed, since contact may stop the body before the falling flag clears.
        private static void Landing(MyCharacter character,bool now,bool flying)
        {
            if(now)
            {
                if(!falling) fallSpeed=0;
                var gravity=character.Physics?.Gravity ?? Vector3.Zero;
                if(gravity.LengthSquared()>1e-4f) fallSpeed=System.Math.Max(fallSpeed,Vector3.Dot(character.Physics.LinearVelocity,Vector3.Normalize(gravity)));
            }
            else if(falling && !flying && fallSpeed>LandingMin)
                Pulse(.05f,60,MathHelper.Lerp(.15f,.4f,MathHelper.Clamp((fallSpeed-LandingMin)/(LandingFull-LandingMin),0,1)));
            falling=now;
        }
        private static void Pulse(float duration,float frequency,float amplitude)
        {
            Player.HandL.Vibrate(0,duration,frequency,amplitude);
            Player.HandR.Vibrate(0,duration,frequency,amplitude);
        }
    }
}
