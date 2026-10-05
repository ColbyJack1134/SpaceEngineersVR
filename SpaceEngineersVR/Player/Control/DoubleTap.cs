using System;

namespace SpaceEngineersVR.Player.Control
{
    // A single tap is reported only after the window passes without a second press, so it never precedes a double.
    internal sealed class DoubleTap
    {
        public const double Window=.15;
        private DateTime? first;
        private bool second;
        public void Reset() { first=null; second=false; }
        // 1 for a single tap, 2 for a double tap, 0 otherwise. A second press that ends as a hold cancels both.
        public int Update(bool available,bool pressed,bool held,bool tapped,DateTime now)
        {
            if(!available) { Reset(); return 0; }
            if(tapped)
            {
                if(second) { Reset(); return 2; }
                first=now; return 0;
            }
            if(!first.HasValue) return 0;
            if(second) { if(!held) Reset(); return 0; }
            if(pressed && (now-first.Value).TotalSeconds<=Window) { second=true; return 0; }
            if((now-first.Value).TotalSeconds<=Window) return 0;
            Reset(); return 1;
        }
    }
}
