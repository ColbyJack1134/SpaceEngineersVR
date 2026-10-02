namespace SpaceEngineersVR.Player.Control
{
    internal sealed class InputGate
    {
        private bool blocked = true;
        internal bool Ready => !blocked;
        public bool Held { get; private set; }
        public bool Pressed { get; private set; }
        public bool Released { get; private set; }

        public void Block()
        {
            blocked = true;
            Held = Pressed = Released = false;
        }

        public void Update(bool active, bool actuated, bool continueHeld = false)
        {
            if (!active) { Block(); return; }
            // An inactive action set is not evidence that its physical control was released.
            if (!actuated || continueHeld) blocked = false;
            bool next = actuated && !blocked;
            Pressed = next && !Held;
            Released = !next && Held;
            Held = next;
        }
    }
}
