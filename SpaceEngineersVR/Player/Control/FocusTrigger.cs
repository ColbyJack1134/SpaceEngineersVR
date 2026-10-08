namespace SpaceEngineersVR.Player.Control
{
    internal sealed class FocusTrigger
    {
        private readonly InputGate press=new InputGate();
        private bool consumed;
        internal bool Suppress { get; private set; }

        internal bool Update(bool eligible,bool focused,bool active,bool down)
        {
            press.Update(eligible && active,down);
            Suppress=consumed;
            if(consumed && active && !down) consumed=false;
            bool request=eligible && !focused && press.Pressed && !Suppress;
            if(request) consumed=Suppress=true;
            return request;
        }
    }
}
