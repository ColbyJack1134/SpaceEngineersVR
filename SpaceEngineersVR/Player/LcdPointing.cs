using System;

namespace SpaceEngineersVR.Player
{
    // Brief hit gaps retain only the visual pose, never input ownership.
    internal sealed class LcdPointing
    {
        private DateTime until;
        internal bool Active(DateTime now) => now<until;
        internal void Reset() { until=DateTime.MinValue; }
        internal void Update(bool available,bool hit,DateTime now)
        {
            if(!available) Reset();
            else if(hit) until=now.AddMilliseconds(120);
        }
    }
}
