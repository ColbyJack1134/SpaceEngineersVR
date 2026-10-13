using System;

namespace SpaceEngineersVR.Multiplayer
{
    // Token bucket for one sender's requests of one kind.
    internal sealed class RequestBudget
    {
        // Human edits arrive a few times per second at most; the legacy cover migration sends one request per open
        // cover in a single frame, so the edit burst covers a full seat. Analog updates run at 20 Hz per hand.
        internal const double EditRate=10,EditBurst=CockpitMemory.MaximumControls+8,AnalogRate=50,AnalogBurst=100;
        private readonly double rate,burst;
        private double tokens,updated;
        internal bool Reported;
        internal RequestBudget(double rate,double burst) { this.rate=rate; this.burst=burst; tokens=burst; }
        internal bool Allow(double now)
        {
            if(updated>0) tokens=Math.Min(burst,tokens+(now-updated)*rate);
            updated=now;
            if(tokens<1) return false;
            tokens-=1; return true;
        }
    }
    internal sealed class SenderBudget
    {
        internal readonly RequestBudget Edits=new RequestBudget(RequestBudget.EditRate,RequestBudget.EditBurst);
        internal readonly RequestBudget Analog=new RequestBudget(RequestBudget.AnalogRate,RequestBudget.AnalogBurst);
    }
}
