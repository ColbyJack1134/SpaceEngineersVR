namespace SpaceEngineersVR.Player.Control
{
    // A squeeze belongs to its first owner until physically released.
    internal sealed class GripCapture
    {
        private bool armed, wasDown;
        private readonly HoldGesture gesture=new HoldGesture();
        public bool Held { get; private set; }
        public bool Consumed { get; private set; }
        public bool AnalogDown(float gated,float raw) => Consumed ? raw>0.1f : gated>0.5f;
        public void Release()
        {
            Held = false;
            gesture.Reset();
            armed = false;
        }
        public bool Update(bool available, bool down, bool near, bool inReach,bool tapHold=false,double now=0)
        {
            bool physicalDown=down;
            if(!available || !inReach) gesture.Reset();
            down=gesture.Update(down,Held,tapHold && available && inReach,now);
            bool captured = false;
            if (!down)
            {
                Held=wasDown=false; Consumed=physicalDown;
                armed=available && !physicalDown;
                return false;
            }
            if (!available || !inReach) Release();
            if (available && inReach && armed && !wasDown && near)
            {
                Held = Consumed = captured = true;
            }
            wasDown = true;
            return captured;
        }
    }
}
