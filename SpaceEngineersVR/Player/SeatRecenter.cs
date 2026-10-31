using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.World;

namespace SpaceEngineersVR.Player
{
    // Face forward after sitting down or spawning a new character.
    internal static class SeatRecenter
    {
        private static object controlled;
        private static long character;
        private static bool spawned;

        internal static void Update()
        {
            var current=MySession.Static?.ControlledEntity;
            long local=MySession.Static?.LocalCharacter?.EntityId ?? 0;
            if(local!=character) { character=local; spawned=local!=0; }
            if(ReferenceEquals(current,controlled)) return;
            // Seats only: returning from a turret, camera or remote grid keeps the current view.
            if(current is MyCockpit && (controlled==null || controlled is MyCharacter)) Player.Headset.RequestRecenter();
            // A character spawned seated keeps the view when it stands up.
            if(spawned && current is MyCharacter) Player.Headset.RequestRecenter();
            spawned=false;
            controlled=current;
        }
        internal static void Reset() { controlled=null; character=0; spawned=false; }
    }
}
