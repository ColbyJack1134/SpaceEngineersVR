using System;

namespace SpaceEngineersVR.Player.Control
{
    internal sealed class VisorGesture
    {
        private readonly InputGate grip=new InputGate();
        private DateTime? pending;

        public bool Update(bool active,bool inside,bool actuated,bool defer,bool panRequested,DateTime now)
        {
            grip.Update(active && !panRequested,actuated);
            if(!active || !inside || panRequested) { pending=null; return false; }
            if(grip.Pressed) pending=now;
            if(!pending.HasValue || defer && (now-pending.Value).TotalSeconds<GripDescent.Delay) return false;
            pending=null;
            return true;
        }
    }
}
