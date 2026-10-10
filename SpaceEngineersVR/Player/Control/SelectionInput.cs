namespace SpaceEngineersVR.Player.Control
{
    internal sealed class SelectionInput
    {
        private readonly InputGate trigger=new InputGate();
        internal bool Active { get; private set; }
        internal void Arm() { Active=true; trigger.Block(); }
        internal void Clear() { Active=false; trigger.Block(); }
        internal bool Update(bool available,bool down,bool uiOwns,bool cancel)
        {
            if(!Active) return false;
            if(!available || cancel) { Clear(); return false; }
            trigger.Update(!uiOwns,down);
            return trigger.Pressed;
        }
    }
}
