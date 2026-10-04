namespace SpaceEngineersVR.Player
{
    // The hand aimed at a UI drives its laser. The current hand keeps it while both are aimed or while its press is held,
    // so the other hand's trigger and grip stay free for modifiers.
    internal sealed class PointerHand
    {
        public bool Left { get; private set; }
        public Controller Hand => Left ? Player.HandL : Player.HandR;
        public Controller Other => Left ? Player.HandR : Player.HandL;
        public void Update(bool rightOn,bool leftOn,bool held)
        {
            if(held) return;
            if(Left ? !leftOn && rightOn : !rightOn && leftOn) Left=!Left;
        }
        public void Reset() => Left=false;
        // Left trigger is ascend while flying, so the left laser only clicks in menus or on foot.
        internal static bool LeftRayAllowed => InputRouter.Mode==InputMode.Menu || !InputRouter.Flying;
    }
}
