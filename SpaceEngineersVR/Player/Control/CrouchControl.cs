using System;
using VRageMath;

namespace SpaceEngineersVR.Player.Control
{
    // Right stick down crouches, up stands and then jumps, plus automatic crouch from real head height. All heights are metres below standing eye height.
    internal sealed class CrouchControl
    {
        public const float StickPress=.7f,StickRelease=.3f,CrouchAt=.7f,StandAt=.4f;
        public const double Settle=.5;
        private bool stickArmed,automatic,suppressed;
        private DateTime pendingUntil;
        public bool Automatic => automatic;
        public bool Jump { get; private set; }

        // Returns true when the native crouch toggle should run; Jump is set instead when an up push has nothing to stand from.
        public bool Update(Vector2 stick,bool stickAllowed,bool physicalAllowed,float headDrop,float depth,bool crouching,DateTime now)
        {
            Jump=false;
            if(Math.Abs(stick.Y)<StickRelease) stickArmed=stickAllowed;
            bool settled=now>=pendingUntil;
            if(settled && !crouching) automatic=false;
            if(!physicalAllowed || depth<=0 || headDrop<=depth*StandAt) suppressed=false;
            bool toggle=false;
            bool vertical=Math.Abs(stick.Y)>StickPress && Math.Abs(stick.Y)>Math.Abs(stick.X);
            if(stickAllowed && stickArmed && vertical && (stick.Y<0)!=crouching)
            {
                stickArmed=false; toggle=true;
                // Standing up by stick while physically low must not immediately re-crouch.
                if(crouching) suppressed=physicalAllowed && headDrop>depth*StandAt;
                automatic=false;
            }
            else if(stickAllowed && stickArmed && vertical && stick.Y>0)
            {
                stickArmed=false; Jump=true;
            }
            else if(physicalAllowed && settled && depth>0)
            {
                if(!crouching && !suppressed && headDrop>=depth*CrouchAt) { toggle=true; automatic=true; }
                else if(crouching && automatic && headDrop<=depth*StandAt) { toggle=true; automatic=false; }
            }
            if(!physicalAllowed && settled) automatic=false;
            if(toggle) pendingUntil=now.AddSeconds(Settle);
            return toggle;
        }

        // Stick crouch lowers the view to the crouched eye; real crouching already lowers it.
        public static float ViewDrop(bool crouching,bool automatic,float headDrop,float depth) =>
            crouching && !automatic ? Math.Max(0,depth-Math.Max(0,headDrop)) : 0;

        public void Reset() { stickArmed=false; automatic=false; suppressed=false; Jump=false; pendingUntil=DateTime.MinValue; }
    }
}
