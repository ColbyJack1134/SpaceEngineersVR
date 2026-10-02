using System;

namespace SpaceEngineersVR.Player.Control
{
    internal sealed class JumpHold
    {
        private bool pending;
        private DateTime started;
        public void Reset() { pending=false; }
        public bool Update(bool available,bool pressed,bool held,DateTime now)
        {
            if(!available || !held) { Reset(); return false; }
            if(pressed) { pending=true; started=now; }
            if(!pending || (now-started).TotalSeconds<.5) return false;
            Reset();
            return true;
        }
    }
}
