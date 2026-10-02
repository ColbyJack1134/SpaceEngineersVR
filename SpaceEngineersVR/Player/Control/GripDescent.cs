using System;

namespace SpaceEngineersVR.Player.Control
{
    internal sealed class GripDescent
    {
        private DateTime? pressed;
        public bool Ready { get; private set; }
        public void Update(bool available,float left,DateTime now)
        {
            if(!available || left<=.025f || float.IsNaN(left)) { pressed=null; Ready=false; return; }
            if(!pressed.HasValue) pressed=now;
            Ready=(now-pressed.Value).TotalSeconds>=.15;
        }
    }
}
