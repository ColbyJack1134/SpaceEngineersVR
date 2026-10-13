using System;

namespace SpaceEngineersVR.Multiplayer
{
    // Token bucket for one sender's cockpit requests. Human edits arrive a few times per second at most; the
    // legacy cover migration sends one request per open cover in a single frame, so the burst covers a full seat.
    internal sealed class RequestBudget
    {
        internal const double Rate=10,Burst=CockpitMemory.MaximumControls+8;
        private double tokens=Burst,updated;
        internal bool Reported;
        internal bool Allow(double now)
        {
            if(updated>0) tokens=Math.Min(Burst,tokens+(now-updated)*Rate);
            updated=now;
            if(tokens<1) return false;
            tokens-=1; return true;
        }
    }
}
