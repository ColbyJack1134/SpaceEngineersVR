using Sandbox.Game.World;
using SpaceEngineersVR.Plugin;

namespace SpaceEngineersVR.Player
{
    internal static class Handedness
    {
        // Latched before input polling so a frame never mixes layouts.
        internal static bool Swapped { get; private set; }
        internal static bool SeatedBuilding { get; private set; }
        internal static Controller ActionHand => Swapped ? Player.HandL : Player.HandR;
        internal static Controller SupportHand => Swapped ? Player.HandR : Player.HandL;

        internal static void Update()
        {
            var session=MySession.Static;
            bool cockpit=CockpitBuilding.Active;
            bool character=session?.LocalCharacter!=null && session.ControlledEntity==session.LocalCharacter && !SpectatorView.Active;
            bool swapped=Common.Config.LeftHanded && (character || cockpit);
            bool seated=swapped && cockpit;
            if(swapped==Swapped && seated==SeatedBuilding) return;
            Swapped=swapped; SeatedBuilding=seated;
            InputRouter.Reset();
        }
    }
}
