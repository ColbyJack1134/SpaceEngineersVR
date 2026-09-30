using System;
using System.Linq;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Gui;
using Sandbox.Game.Screens.Helpers;
using Sandbox.Game.World;
using Sandbox.Graphics.GUI;
using Sandbox.ModAPI;
using SpaceEngineersVR.Config;
using SpaceEngineersVR.Plugin;
using VRage.Game;

namespace SpaceEngineersVR.Player
{
    // Native button-panel assignment/activation, independent of the ship hotbar.
    internal static class CockpitActions
    {
        private static MyCockpit owner;
        private static MyToolbar toolbar,previous;
        private static string world;
        private static bool editing,previousAutoUpdate;
        private static MyGuiScreenBase editor;
        private static GUI.CockpitAssignment assignment;
        public static MyToolbar Toolbar => toolbar;
        public static void Update(MyCockpit seat)
        {
            assignment?.Update();
            if(ReferenceEquals(owner,seat)) return;
            Reset();
            if(seat==null) return;
            owner=seat; world=MySession.Static.CurrentPath;
            int count=CockpitLayout.Count(seat.BlockDefinition.Id.SubtypeName);
            toolbar=new MyToolbar(MyToolbarType.ButtonPanel,Math.Min(9,count),(count+8)/9);
            var stored=Common.Config.CockpitActions.FirstOrDefault(s=>s.World==world && s.Cockpit==seat.EntityId);
            MyObjectBuilder_Toolbar builder=null;
            if(!string.IsNullOrEmpty(stored?.ToolbarXml))
                try { builder=MyAPIGateway.Utilities.SerializeFromXML<MyObjectBuilder_Toolbar>(stored.ToolbarXml); }
                catch(Exception ex) { Logger.Warning(ex,"Cockpit switch assignments could not be read; original config retained"); }
            toolbar.Init(builder,seat);
            toolbar.ItemChanged+=Changed;
        }
        private static void Changed(MyToolbar source,MyToolbar.IndexArgs index,bool gamepad)
        {
            if(!ReferenceEquals(source,toolbar) || owner==null) return;
            if(index.ItemIndex>=CockpitLayout.Count(owner.BlockDefinition.Id.SubtypeName))
            {
                if(source.GetItemAtIndex(index.ItemIndex)!=null) source.SetItemAtIndex(index.ItemIndex,null);
                return;
            }
            var entry=new CockpitActionSetting { World=world,Cockpit=owner.EntityId,
                ToolbarXml=MyAPIGateway.Utilities.SerializeToXML(source.GetObjectBuilder()) };
            Common.Config.CockpitActions=Common.Config.CockpitActions.Where(s=>s.World!=world || s.Cockpit!=owner.EntityId).Concat(new[] { entry }).ToArray();
        }
        public static bool Activate(int slot,bool? desired=null)
        {
            if(toolbar==null || owner==null || !SeatFit.Eligible(owner) || editing || !((Sandbox.ModAPI.IMyTerminalBlock)owner).HasPlayerAccess(MySession.Static.LocalPlayerId)) return false;
            toolbar.UpdateItemForIdentity(slot,MySession.Static.LocalPlayerId,false);
            var item=toolbar.GetItemAtIndex(slot);
            if(item==null) { Configure(slot); return false; }
            if(!item.Enabled || (item is MyToolbarItemTerminalGroup group && !group.PlayerHasAccessToAllBlocks(MySession.Static.LocalPlayerId)))
            { EssentialHud.Notify("This switch action is unavailable or access is denied."); return false; }
            bool binary=CockpitSwitchState.Read(item,out _);
            if(!binary && desired==false) return false;
            return binary && desired.HasValue ? CockpitSwitchState.Set(item,desired.Value) : toolbar.ActivateItemAtIndex(slot);
        }
        public static bool ReadState(int slot,out float state)
        {
            state=0;
            if(toolbar==null || owner==null || editing) return false;
            toolbar.UpdateItemForIdentity(slot,MySession.Static.LocalPlayerId,false);
            return CockpitSwitchState.Read(toolbar.GetItemAtIndex(slot),out state);
        }
        public static void Configure(int selected=-1)
        {
            if(owner==null || toolbar==null || editing || MyGuiScreenToolbarConfigBase.Static!=null) return;
            previous=MyToolbarComponent.CurrentToolbar; previousAutoUpdate=MyToolbarComponent.AutoUpdate;
            try
            {
                editing=true; MyToolbarComponent.CurrentToolbar=toolbar; MyToolbarComponent.AutoUpdate=false;
                if(selected>=0) toolbar.SwitchToPage(selected/toolbar.SlotCount);
                editor=MyGuiSandbox.CreateScreen(MyPerGameSettings.GUI.ToolbarConfigScreen,0,owner,null);
                assignment=new GUI.CockpitAssignment((MyGuiScreenToolbarConfigBase)editor,toolbar,previous,
                    CockpitLayout.Count(owner.BlockDefinition.Id.SubtypeName),selected);
                assignment.Update();
                editor.Closed+=Closed;
                MyGuiSandbox.AddScreen(MyGuiScreenGamePlay.ActiveGameplayScreen=editor);
                Controls.Static.BlockUntilRelease();
            }
            catch(Exception ex)
            { RestoreEditor(); Logger.Warning(ex,"Cockpit switch configuration failed"); EssentialHud.Notify("Switch configuration unavailable; see plugin log."); }
        }
        private static void Closed(MyGuiScreenBase screen,bool unloading) { RestoreEditor(); }
        private static void RestoreEditor()
        {
            if(!editing) return;
            editing=false;
            assignment?.Dispose(); assignment=null;
            if(editor!=null) editor.Closed-=Closed;
            editor=null;
            if(ReferenceEquals(MyToolbarComponent.CurrentToolbar,toolbar)) MyToolbarComponent.CurrentToolbar=previous;
            MyToolbarComponent.AutoUpdate=previousAutoUpdate; previous=null;
        }
        public static void Reset()
        {
            if(editor!=null) editor.CloseScreen();
            RestoreEditor();
            if(toolbar!=null)
            {
                if(owner?.HasInventory==true) owner.GetInventory().ContentsChanged-=toolbar.CharacterInventory_OnContentsChanged;
                toolbar.ItemChanged-=Changed; toolbar.Clear();
            }
            toolbar=null; owner=null; world=null;
        }
    }
}
