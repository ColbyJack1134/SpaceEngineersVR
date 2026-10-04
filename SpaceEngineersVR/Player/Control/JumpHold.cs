using System;

namespace SpaceEngineersVR.Player.Control
{
    internal sealed class JumpHold
    {
        private bool pending;
        internal bool Tapped { get; private set; }
        internal bool Alternate { get; private set; }
        private DateTime started;
        public void Reset() { pending=Tapped=Alternate=false; }
        public bool Update(bool available,bool pressed,bool held,DateTime now,bool holdAction=true,bool alternate=false)
        {
            Tapped=false;
            if(!available) { Reset(); return false; }
            if(!held) { Tapped=pending; pending=false; return false; }
            if(pressed) { pending=true; started=now; Alternate=alternate; }
            if(!pending || (now-started).TotalSeconds<.5) return false;
            pending=false;
            return holdAction;
        }
    }
}
