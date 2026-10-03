using System;

namespace SpaceEngineersVR.Player.Control
{
    internal sealed class GripDescent
    {
        private DateTime? pressed;
        private bool emitted;
        public bool Ready { get; private set; }
        public bool TakePress(bool down)
        {
            if(!down) { emitted=false; return false; }
            if(!Ready || emitted) return false;
            emitted=true; return true;
        }
        public void Update(bool available,float left,DateTime now)
        {
            if(!available || left<=.025f || float.IsNaN(left)) { pressed=null; Ready=false; emitted=false; return; }
            if(!pressed.HasValue) pressed=now;
            Ready=(now-pressed.Value).TotalSeconds>=.15;
        }
    }
}
