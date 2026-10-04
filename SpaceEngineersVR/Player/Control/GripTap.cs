using System;

namespace SpaceEngineersVR.Player.Control
{
    internal sealed class GripTap
    {
        public const double MaxSeconds=.4;
        private DateTime? pressed;
        private bool cancelled;
        // A blocked button drops Held without Released, so view gestures never complete a tap.
        public bool Update(bool held,bool released,bool cancel,DateTime now)
        {
            if(held)
            {
                if(!pressed.HasValue) { pressed=now; cancelled=false; }
                if(cancel || (now-pressed.Value).TotalSeconds>MaxSeconds) cancelled=true;
                return false;
            }
            bool tap=released && pressed.HasValue && !cancelled && (now-pressed.Value).TotalSeconds<=MaxSeconds;
            pressed=null;
            return tap;
        }
    }
}
