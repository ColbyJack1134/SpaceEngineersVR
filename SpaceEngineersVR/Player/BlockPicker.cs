using System;
using System.Reflection;
using HarmonyLib;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.Screens.Helpers;
using Sandbox.Game.World;
using VRage.Game;
using VRage.Game.ObjectBuilders.Components;
using VRage.Utils;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal static class BlockPicker
    {
        private static MyCubeBlockDefinition pending;
        // The build radial's own check: public, DLC, research and newsletter blocks.
        internal static readonly MethodInfo IsBlockEnabled=AccessTools.Method("Sandbox.Game.Screens.Helpers.MyRadialMenuItemCubeBlock:IsBlockEnabled");
        internal static bool Available => IsBlockEnabled!=null;
        // Activation can switch to the last used variant of the block's group, which still counts as the picked block.
        internal static Func<MyCubeBlockDefinition,bool> InHand=definition =>
            MyCubeBuilder.Static?.IsActivated==true && (MyCubeBuilder.Static.CurrentBlockDefinition==definition || MyCubeBuilder.Static.ToolbarBlockDefinition==definition ||
                definition.BlockVariantsGroup!=null && MyCubeBuilder.Static.CurrentBlockDefinition?.BlockVariantsGroup==definition.BlockVariantsGroup);
        // The picked block can be saved to a slot only while it is still the block in hand.
        internal static MyCubeBlockDefinition Pending => pending!=null && InHand(pending) ? pending : (pending=null);
        internal static void SetPending(MyCubeBlockDefinition definition) => pending=definition;

        // Same path as the gamepad build radial, which equips a block without a toolbar slot.
        internal static void Equip(MyCubeBlockDefinition definition)
        {
            var character=MySession.Static?.LocalCharacter;
            if(definition==null || character==null || character.IsDead || MySession.Static.ControlledEntity!=character || MyCubeBuilder.Static==null || !Available) return;
            if(Convert.ToInt32(IsBlockEnabled.Invoke(null,new object[] {definition,null}))!=0)
            { EssentialHud.Notify(definition.DisplayNameText+" cannot be built"); return; }
            if(!MySessionComponentSafeZones.IsActionAllowed(character,MySafeZoneAction.Building,0L,0uL)) return;
            var placer=new MyDefinitionId(typeof(MyObjectBuilder_CubePlacer));
            if(character.CurrentWeapon==null || character.CurrentWeapon.DefinitionId!=placer) character.SwitchToWeapon(placer);
            // The builder toggles size on activation, so it starts from the other size.
            MyCubeBuilder.Static.CubeBuilderState.SetCubeSize(definition.CubeSize==MyCubeSize.Large ? MyCubeSize.Small:MyCubeSize.Large);
            MyCubeBuilder.Static.ActivateFromRadialMenu(definition.Id);
            MyCubeBuilder.Static.SetToolType(MyCubeBuilderToolType.BuildTool);
            pending=definition;
        }

        // Empty slots take the picked block; an occupied slot needs the explicit assign press.
        internal static bool TrySave(MyToolbar toolbar,int slot,bool assign)
        {
            var definition=Pending;
            if(definition==null || toolbar.ToolbarType!=MyToolbarType.Character || slot<0 || slot>=toolbar.SlotCount) { pending=null; return false; }
            if(!assign && toolbar.GetItemAtSlot(slot)!=null) { pending=null; return false; }
            var item=MyToolbarItemFactory.CreateToolbarItem(MyToolbarItemFactory.ObjectBuilderFromDefinition(definition));
            pending=null;
            if(item==null || !item.AllowedInToolbarType(toolbar.ToolbarType)) return false;
            toolbar.SetItemAtSlot(slot,item);
            return true;
        }

        // The ray hit lies on the block surface; step inside so the cell lookup finds the block, not its neighbour.
        internal static MySlimBlock Target(MyCubeGrid grid,LineD ray,Vector3D hit) => grid?.GetTargetedBlock(hit+ray.Direction*grid.GridSize*.1);

        // The welder's projection outline; the selection box lines are too thin at block scale.
        internal static void Outline(MySlimBlock block) =>
            MyCubeBuilder.DrawSemiTransparentBox(block.CubeGrid,block,Color.Green.ToVector4(),true,MyStringId.GetOrCompute("GizmoDrawLine"));
    }
}
