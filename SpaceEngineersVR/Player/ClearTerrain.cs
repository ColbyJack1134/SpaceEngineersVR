using System;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.Weapons;
using Sandbox.Game.World;
using SpaceEngineersVR.Player.Control;
using VRage.Game;

namespace SpaceEngineersVR.Player
{
    // Latches a drill's secondary action (dig without collecting) onto the trigger.
    internal static class ClearTerrain
    {
        private static readonly GripTap tap=new GripTap();
        private static bool latched,gripWas;
        private static long owner,tool;
        private static MyDefinitionId? gun;
        internal static bool Active => latched && Same();
        internal static bool Available => Selected(out _,out _,out _);
        // Read by the HUD draw, which must not walk game entities.
        internal static volatile bool Shown;

        internal static bool Selected(out long controlled,out long weapon,out MyDefinitionId? shipGun)
        {
            controlled=weapon=0; shipGun=null;
            var entity=MySession.Static?.ControlledEntity;
            if(entity is MyCharacter character && character.CurrentWeapon is MyHandDrill drill)
            { controlled=character.EntityId; weapon=drill.EntityId; return true; }
            if(entity is MyShipController ship && ship.GridSelectionSystem?.GetGunId() is MyDefinitionId id && id.TypeId==typeof(MyObjectBuilder_Drill))
            { controlled=ship.EntityId; shipGun=id; return true; }
            return false;
        }
        private static bool Same() => Selected(out long controlled,out long weapon,out var shipGun) && controlled==owner && weapon==tool && shipGun==gun;

        internal static void Toggle()
        {
            if(!latched && !Selected(out owner,out tool,out gun)) return;
            latched=!latched;
            Handedness.ActionHand?.Vibrate(0,.03f,latched ? 160:120,latched ? .4f:.25f);
        }
        // Only a physical release completes a tap; a grip blocked by a gesture drops held without releasing.
        internal static void Update(bool allowed,bool grip,bool physical,bool cancel)
        {
            if(latched && !Same()) latched=false;
            bool held=allowed && grip;
            if(tap.Update(held,gripWas && !physical,cancel,DateTime.UtcNow) && allowed && Available) Toggle();
            gripWas=held;
            Shown=Active;
        }
        internal static void Reset() { latched=gripWas=Shown=false; tap.Update(false,false,true,DateTime.UtcNow); }
    }
}
