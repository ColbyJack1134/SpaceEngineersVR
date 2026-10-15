using Sandbox.Game.Entities.Character;
using Sandbox.Game.World;
using SpaceEngineersVR.Plugin;

namespace SpaceEngineersVR.Player
{
    // A pulse in both hands when the magnetic boots lock to a surface and a lighter one when they let go.
    internal static class BootsHaptics
    {
        private static MyCharacter owner;
        private static bool locked;
        public static void Update()
        {
            var character=MySession.Static?.LocalCharacter;
            bool now=Main.VrActive && character!=null && !character.IsDead && character.IsMagneticBootsActive;
            if(!ReferenceEquals(character,owner) || character?.IsSitting==true) { owner=character; locked=now; return; }
            if(now==locked) return;
            locked=now;
            foreach(var hand in new[] {Player.HandL,Player.HandR})
                if(now) hand.Vibrate(0,.05f,80,.2f); else hand.Vibrate(0,.03f,110,.1f);
        }
    }
}
