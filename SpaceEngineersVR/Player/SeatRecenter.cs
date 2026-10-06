using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.World;

namespace SpaceEngineersVR.Player
{
    // Face forward after sitting down from the character.
    internal static class SeatRecenter
    {
        private static object controlled;

        internal static void Update()
        {
            var current=MySession.Static?.ControlledEntity;
            if(ReferenceEquals(current,controlled)) return;
            // Seats only: returning from a turret, camera or remote grid keeps the current view.
            if(current is MyCockpit && (controlled==null || controlled is MyCharacter)) Player.Headset.RequestRecenter();
            controlled=current;
        }
        internal static void Reset() => controlled=null;
    }
}
