using System;
using System.Linq;
using System.Collections.Generic;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Gui;
using Sandbox.Game.Screens.Helpers;
using Sandbox.Game.World;
using Sandbox.Graphics.GUI;
using Sandbox.ModAPI;
using SpaceEngineersVR.Config;
using SpaceEngineersVR.Plugin;
using SpaceEngineersVR.Multiplayer;
using VRage.Game;

namespace SpaceEngineersVR.Player
{
    // Native button-panel assignment/activation, independent of the ship hotbar.
    internal static class CockpitActions
    {
        private static MyCockpit owner;
        private static MyToolbar toolbar,previous;
        private static string world;
        private static bool editing,previousAutoUpdate,migrated;
        private static string loadedXml,pendingXml;
        private static double pendingUntil;
        internal static bool SharedReady => owner!=null && MultiplayerRuntime.Get(owner,out _);
        private static MyGuiScreenBase editor;
        private static GUI.CockpitAssignment assignment;
        private static CockpitAssignmentToolbar assignmentToolbar;
        private static readonly List<AnalogControl.Channel>[] analog=new List<AnalogControl.Channel>[CockpitLayout.MaximumCount];
        private static readonly double[] nextAnalog=new double[CockpitLayout.MaximumCount];
        public static MyToolbar Toolbar => toolbar;
        internal static bool EditingToolbar(MyToolbar candidate) => editing && ReferenceEquals(candidate,assignmentToolbar?.Toolbar);
        internal static bool ReadAnalog(int slot,out float position,out string label)
        {
            position=0; label=null;
            if(toolbar==null || owner==null || editing || !AnalogControl.IsHandle(owner.BlockDefinition.Id.SubtypeName,slot)) return false;
            if(MultiplayerRuntime.Now>=nextAnalog[slot])
            {
                nextAnalog[slot]=MultiplayerRuntime.Now+.2;
                toolbar.UpdateItemForIdentity(slot,MySession.Static.LocalPlayerId,false);
                var item=toolbar.GetItemAtIndex(slot);
                analog[slot]=AnalogControl.Resolve(item,MySession.Static.LocalPlayerId,false);
            }
            var channels=analog[slot];
            if(channels==null || channels.Count==0) return false;
            position=channels[0].Position(); label=channels[0].Label();
            string first=label;
            if(channels.Skip(1).Any(c=>c.Label()!=first)) label="Mixed";
            return true;
        }
        internal static bool SetAnalog(int slot,float position)
        {
            if(toolbar==null || owner==null || editing || !SeatFit.Eligible(owner) || !SharedReady ||
                !((Sandbox.ModAPI.IMyTerminalBlock)owner).HasPlayerAccess(MySession.Static.LocalPlayerId) ||
                !ReadAnalog(slot,out _,out _)) return false;
            MultiplayerRuntime.SetAnalog(owner,slot,position);
            return true;
        }
        internal static string AnalogLabel(int slot,float position) => analog[slot]?.FirstOrDefault()?.Label(position);

        public static void Update(MyCockpit seat)
        {
            assignment?.Update();
            if(ReferenceEquals(owner,seat)) { Synchronize(); return; }
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
            CockpitMemory.UpgradeToolbar(builder,seat.BlockDefinition.Id.SubtypeName,stored?.LayoutVersion ?? 1);
            toolbar.Init(builder,seat);
            loadedXml=builder==null ? "" : CockpitMemory.Toolbar(builder);
            toolbar.ItemChanged+=Changed;
            Synchronize();
        }
        private static void Synchronize()
        {
            if(owner==null || toolbar==null || !MultiplayerRuntime.Get(owner,out var shared)) return;
            if(!migrated)
            {
                migrated=true;
                if(shared.Revision==0 && string.IsNullOrEmpty(shared.Toolbar) && shared.Covers.Length==0)
                {
                    if(!string.IsNullOrEmpty(loadedXml)) SaveToolbar();
                    var old=LoadLocal(owner).Covers;
                    if(old!=null) for(int i=0;i<Math.Min(old.Length,CockpitLayout.Count(owner.BlockDefinition.Id.SubtypeName));i++)
                        if(old[i]) MultiplayerRuntime.SaveCover(owner,i,true);
                }
            }
            if(pendingXml!=null)
            {
                if(shared.Toolbar!=pendingXml && MultiplayerRuntime.Now<pendingUntil) return;
                pendingXml=null;
            }
            if(editing || shared.Toolbar==loadedXml) return;
            var builder=CockpitMemory.Toolbar(shared.Toolbar);
            toolbar.ItemChanged-=Changed;
            try
            {
                if(owner.HasInventory) owner.GetInventory().ContentsChanged-=toolbar.CharacterInventory_OnContentsChanged;
                toolbar.Clear(); toolbar.Init(builder,owner); loadedXml=shared.Toolbar;
                Array.Clear(analog,0,analog.Length); Array.Clear(nextAnalog,0,nextAnalog.Length);
            }
            finally { toolbar.ItemChanged+=Changed; }
        }
        private static void Changed(MyToolbar source,MyToolbar.IndexArgs index,bool gamepad)
        {
            if(!ReferenceEquals(source,toolbar) || owner==null) return;
            if(index.ItemIndex>=CockpitLayout.Count(owner.BlockDefinition.Id.SubtypeName))
            {
                if(source.GetItemAtIndex(index.ItemIndex)!=null) source.SetItemAtIndex(index.ItemIndex,null);
                return;
            }
            Array.Clear(nextAnalog,0,nextAnalog.Length);
            loadedXml=CockpitMemory.Toolbar(source.GetObjectBuilder());
            SaveToolbar();
        }
        private static void SaveToolbar()
        {
            pendingXml=loadedXml; pendingUntil=MultiplayerRuntime.Now+5;
            MultiplayerRuntime.SaveToolbar(owner,loadedXml);
        }
        public static bool Activate(int slot,bool? desired=null)
        {
            long started=FeatureTiming.Start();
            try { return ActivateCore(slot,desired); }
            finally { FeatureTiming.End(FeatureTiming.Area.CockpitAction,started); }
        }
        private static bool ActivateCore(int slot,bool? desired)
        {
            if(toolbar==null || owner==null || !SeatFit.Eligible(owner) || editing || !((Sandbox.ModAPI.IMyTerminalBlock)owner).HasPlayerAccess(MySession.Static.LocalPlayerId)) return false;
            if(!SharedReady) { EssentialHud.Notify("Shared cockpit controls require SEVR on the host."); return false; }
            toolbar.UpdateItemForIdentity(slot,MySession.Static.LocalPlayerId,false);
            var item=toolbar.GetItemAtIndex(slot);
            if(item==null) { Configure(slot); return false; }
            bool view=CockpitSwitchState.ViewState(item,out bool active);
            // Native Control becomes disabled while occupied; the owning switch must still release it.
            if(view && active)
            {
                if(desired!=true) RemoteView.Exit();
                return true;
            }
            var controller=MySession.Static.ControlledEntity as MyShipController;
            bool weapon=CockpitSwitchState.WeaponState(item,controller,out bool selected);
            if(weapon && (selected || desired==false))
            {
                // Repeated selection cycles ammunition in vanilla. Down clears only this weapon's selection.
                if(selected && desired!=true) controller.SwitchToWeapon((MyToolbarItemWeapon)null);
                return true;
            }
            if(!item.Enabled || (item is MyToolbarItemTerminalGroup group && !group.PlayerHasAccessToAllBlocks(MySession.Static.LocalPlayerId)))
            { EssentialHud.Notify("This switch action is unavailable or access is denied."); return false; }
            if(view)
            {
                if(desired==false) return false;
                if(item is MyToolbarItemTerminalBlock cameraItem && cameraItem.Block is MyCameraBlock camera && RemoteView.Turret)
                    return RemoteView.SwitchToCamera(camera);
                return toolbar.ActivateItemAtIndex(slot);
            }
            if(weapon) return controller!=null && toolbar.ActivateItemAtIndex(slot);
            bool stateful=CockpitSwitchState.Read(item,out _);
            if(!stateful && desired==false) return false;
            return stateful && desired.HasValue ? CockpitSwitchState.Set(item,desired.Value) : toolbar.ActivateItemAtIndex(slot);
        }
        private static readonly List<Sandbox.ModAPI.Ingame.IMyTerminalBlock> rankBlocks=new List<Sandbox.ModAPI.Ingame.IMyTerminalBlock>();
        private static bool Handle(int slot) => owner!=null && AnalogControl.IsHandle(owner.BlockDefinition.Id.SubtypeName,slot);
        private static bool AllBlocks(MyToolbarItemActions item,Predicate<Sandbox.ModAPI.Ingame.IMyTerminalBlock> test)
        {
            rankBlocks.Clear();
            if(item is MyToolbarItemTerminalBlock block) block.FetchAllBlocks(rankBlocks);
            else if(item is MyToolbarItemTerminalGroup group) group.FetchAllBlocks(rankBlocks);
            return rankBlocks.Count>0 && rankBlocks.TrueForAll(b=>b!=null && test(b));
        }
        internal static bool Analog(int slot,MyToolbarItemActions item) => Handle(slot) && AllBlocks(item,b=>AnalogControl.Resolve(b,item.ActionId)!=null);
        internal static bool Compatible(int slot,MyToolbarItemActions item,string action) => owner!=null &&
            AllBlocks(item,b=>Handle(slot) ? AnalogControl.Resolve(b,action)!=null : CockpitSwitchState.ShowsReady(b,action) || CockpitSwitchState.ViewAction(action,b) || CockpitSwitchState.BatteryAction(b,action));
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
            if(!SharedReady) { EssentialHud.Notify("Shared cockpit controls require SEVR on the host."); return; }
            previous=MyToolbarComponent.CurrentToolbar; previousAutoUpdate=MyToolbarComponent.AutoUpdate;
            try
            {
                editing=true;
                assignmentToolbar=new CockpitAssignmentToolbar(toolbar,CockpitAssignmentLayout.Find(owner.BlockDefinition.Id.SubtypeName));
                var target=assignmentToolbar.Toolbar;
                selected=assignmentToolbar.Layout.DisplayIndex(selected);
                MyToolbarComponent.CurrentToolbar=target; MyToolbarComponent.AutoUpdate=false;
                if(selected>=0) target.SwitchToPage(selected/target.SlotCount);
                editor=MyGuiSandbox.CreateScreen(MyPerGameSettings.GUI.ToolbarConfigScreen,0,owner,null);
                assignment=new GUI.CockpitAssignment((MyGuiScreenToolbarConfigBase)editor,target,previous,
                    target.SlotCount*target.PageCount,selected,layout:assignmentToolbar.Layout);
                assignment.Update();
                editor.Closed+=Closed;
                MyGuiSandbox.AddScreen(MyGuiScreenGamePlay.ActiveGameplayScreen=editor);
                Controls.Static.BlockUntilRelease();
            }
            catch(Exception ex)
            { RestoreEditor(); Logger.Warning(ex,"Cockpit switch configuration failed"); EssentialHud.Notify("Switch configuration unavailable"); }
        }
        private static void Closed(MyGuiScreenBase screen,bool unloading) { RestoreEditor(); }
        private static void RestoreEditor()
        {
            if(!editing) return;
            editing=false;
            assignment?.Dispose(); assignment=null;
            if(editor!=null) editor.Closed-=Closed;
            editor=null;
            if(ReferenceEquals(MyToolbarComponent.CurrentToolbar,assignmentToolbar?.Toolbar)) MyToolbarComponent.CurrentToolbar=previous;
            assignmentToolbar?.Dispose(); assignmentToolbar=null;
            MyToolbarComponent.AutoUpdate=previousAutoUpdate; previous=null;
        }
        internal static CockpitMemory.Record LoadLocal(MyCockpit seat)
        {
            string path=MySession.Static.CurrentPath;
            var record=new CockpitMemory.Record { Revision=1 };
            var stored=Common.Config.CockpitActions.FirstOrDefault(s=>s.World==path && s.Cockpit==seat.EntityId);
            try
            {
                string xml=stored?.ToolbarXml;
                var builder=CockpitMemory.Toolbar(xml);
                CockpitMemory.UpgradeToolbar(builder,seat.BlockDefinition.Id.SubtypeName,stored?.LayoutVersion ?? 1);
                if(builder!=null) record.Toolbar=CockpitMemory.Toolbar(builder);
            }
            catch(Exception ex) { Logger.Warning(ex,"Local cockpit switch assignments could not be read"); }
            var state=Common.Config.CockpitStates.FirstOrDefault(s=>s.World==path && s.Cockpit==seat.EntityId);
            if(state?.Covers!=null)
            {
                var old=new CockpitMemory.Record { Covers=(bool[])state.Covers.Clone(),LayoutVersion=state.LayoutVersion };
                CockpitMemory.Upgrade(old,seat.BlockDefinition.Id.SubtypeName); record.Covers=old.Covers;
            }
            return record;
        }
        internal static void SaveLocal(MyCockpit seat,CockpitMemory.Record record)
        {
            string path=MySession.Static.CurrentPath; long id=seat.EntityId;
            var config=Common.Config;
            config.CockpitActions=config.CockpitActions.Where(s=>s.World!=path || s.Cockpit!=id)
                .Append(new CockpitActionSetting { World=path,Cockpit=id,ToolbarXml=record.Toolbar,LayoutVersion=CockpitMemory.CurrentLayout }).ToArray();
            config.CockpitStates=config.CockpitStates.Where(s=>s.World!=path || s.Cockpit!=id)
                .Append(new CockpitStateSetting { World=path,Cockpit=id,Covers=(bool[])record.Covers.Clone(),LayoutVersion=CockpitMemory.CurrentLayout }).ToArray();
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
            Array.Clear(analog,0,analog.Length); Array.Clear(nextAnalog,0,nextAnalog.Length);
            toolbar=null; owner=null; world=null; loadedXml=pendingXml=null; pendingUntil=0; migrated=false;
        }
    }
}
