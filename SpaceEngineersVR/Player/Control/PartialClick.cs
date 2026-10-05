using System;

namespace SpaceEngineersVR.Player.Control
{
    // Menu clicks end on a partial release and rearm on a partial squeeze, so double-clicking does not
    // need the trigger fully let go. The binding's full release still applies.
    internal sealed class PartialClick
    {
        internal const float Drop=.3f,Rise=.25f;
        private float peak,trough;
        private bool rearmed;
        public bool Held { get; private set; }
        public bool Pressed { get; private set; }
        public void Update(bool down,bool newPress,float pressure)
        {
            Pressed=false;
            if(!down || float.IsNaN(pressure)) { Held=rearmed=false; return; }
            if(Held)
            {
                peak=Math.Max(peak,pressure);
                if(peak-pressure>=Drop) { Held=false; rearmed=true; trough=pressure; }
                return;
            }
            trough=Math.Min(trough,pressure);
            if(newPress || rearmed && pressure-trough>=Rise) { Held=Pressed=true; rearmed=false; peak=pressure; }
        }
    }
}
