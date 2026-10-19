using System;
using System.Linq;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.Gui;
using Sandbox.Game.Screens.Helpers;
using Sandbox.Game.World;
using Sandbox.Graphics.GUI;
using Sandbox.ModAPI;
using SpaceEngineersVR.Config;
using SpaceEngineersVR.Multiplayer;
using SpaceEngineersVR.Plugin;
using VRage.Game;
using VRage.Game.Entity;
using Controllable = Sandbox.Game.Entities.IMyControllableEntity;

namespace SpaceEngineersVR.Player
{
    // Stored by SEVR: the native gamepad slots already hold items vanilla copies from the toolbar and from prefabs.
    internal static class QuickToolbar
    {
        internal const int Slots=4;
        internal static readonly string[] Names={"Up","Right","Down","Left"};
        private const double HoldSeconds=.5;
        private static readonly DateTime[] pressedAt=new DateTime[Slots];
        private static readonly bool[] held=new bool[Slots];
        private static Store store;
        private static MyToolbar previous;
        private static bool previousAutoUpdate,editing;
        private static MyGuiScreenBase editor;

        internal sealed class Store : IDisposable
        {
            internal readonly MyToolbar Toolbar;
            internal readonly Controllable Entity;
            internal readonly string World;
            private readonly bool character;
            private readonly long owner;
            internal Store(Controllable entity,string world)
            {
                Entity=entity; World=world;
                character=entity is MyCharacter;
                owner=character ? MySession.Static.LocalPlayerId : ((MyEntity)entity).EntityId;
                Toolbar=Create(entity.Toolbar?.ToolbarType ?? MyToolbarType.Character);
                var saved=Common.Config.QuickToolbars.FirstOrDefault(Matches);
                MyObjectBuilder_Toolbar builder=null;
                try { builder=CockpitMemory.Toolbar(saved?.ToolbarXml); }
                catch(Exception ex) { Logger.Warning(ex,"Quick toolbar could not be read; slots start empty"); }
                Toolbar.Init(builder,(MyEntity)entity);
                Toolbar.ItemChanged+=Changed;
            }
            // Character toolbars start with the game's default blocks.
            internal static MyToolbar Create(MyToolbarType type)
            {
                var toolbar=new MyToolbar(type,Slots,1);
                toolbar.Clear();
                return toolbar;
            }
            private bool Matches(QuickToolbarSetting s) => s!=null && s.World==World && s.Character==character && s.Owner==owner;
            internal static string Serialize(MyToolbar toolbar)
            {
                var builder=toolbar.GetObjectBuilder();
                builder.ToolbarType=toolbar.ToolbarType;
                return ((VRage.Game.ModAPI.IMyUtilities)MyAPIUtilities.Static).SerializeToXML(builder);
            }
            private void Changed(MyToolbar source,MyToolbar.IndexArgs args,bool gamepad)
            {
                var value=new QuickToolbarSetting { World=World,Character=character,Owner=owner,ToolbarXml=Serialize(Toolbar) };
                Common.Config.QuickToolbars=Common.Config.QuickToolbars.Where(s=>!Matches(s)).Append(value).ToArray();
            }
            public void Dispose()
            {
                Toolbar.ItemChanged-=Changed;
                MultiplayerRuntime.Detach(Toolbar);
                Toolbar.Clear();
            }
        }

        public static void Update()
        {
            var controls=Controls.Static;
            var entity=MySession.Static?.ControlledEntity;
            if(!editing && (store==null || !ReferenceEquals(store.Entity,entity) || store.World!=MySession.Static?.CurrentPath))
            {
                store?.Dispose();
                store=entity?.Toolbar!=null ? new Store(entity,MySession.Static.CurrentPath) : null;
            }
            bool available=store!=null && InputRouter.Gameplay && !Main.MenuOpen && !InputRouter.RadialOpen && !editing && MySession.Static?.LocalCharacter?.IsDead==false;
            var now=DateTime.UtcNow;
            for(int i=0;i<Slots;i++)
            {
                var button=controls.QuickSlots[i];
                if(!available) { held[i]=false; continue; }
                if(button.HasPressed) { held[i]=true; pressedAt[i]=now; }
                else if(held[i] && button.IsPressed && (now-pressedAt[i]).TotalSeconds>=HoldSeconds) { held[i]=false; Hide(); Configure(i); return; }
                else if(held[i] && button.HasReleased) { held[i]=false; Use(i); }
                else if(!button.IsPressed) held[i]=false;
            }
            Publish(available);
        }
        internal enum Result { Assign, Activated, Unavailable }
        internal static Result Tap(MyToolbarItem item) => item==null ? Result.Assign : item.Enabled ? Result.Activated : Result.Unavailable;
        private static void Use(int slot)
        {
            store.Toolbar.UpdateItemForIdentity(slot,MySession.Static.LocalPlayerId,false);
            var result=Tap(store.Toolbar.GetItemAtIndex(slot));
            if(result==Result.Assign) { Hide(); Configure(slot); return; }
            if(result==Result.Unavailable) { EssentialHud.Notify("Action unavailable"); return; }
            store.Toolbar.ActivateItemAtSlot(slot);
            Player.HandL.Vibrate(0,.025f,90,.2f);
            GameActions.Reset();
            InputRouter.Update();
        }
        private static void Configure(int slot)
        {
            if(editing || MyGuiScreenToolbarConfigBase.Static!=null) return;
            previous=MyToolbarComponent.CurrentToolbar; previousAutoUpdate=MyToolbarComponent.AutoUpdate;
            try
            {
                editing=true;
                MyToolbarComponent.CurrentToolbar=store.Toolbar; MyToolbarComponent.AutoUpdate=false;
                editor=MyGuiSandbox.CreateScreen(MyPerGameSettings.GUI.ToolbarConfigScreen,0,store.Entity as MyCubeBlock,null);
                new GUI.CockpitAssignment((MyGuiScreenToolbarConfigBase)editor,store.Toolbar,store.Entity.Toolbar,Slots,slot,switches:false,
                    title:"Quick toolbar · "+Names[slot],holster:false).Update();
                editor.Closed+=Closed;
                MyGuiSandbox.AddScreen(MyGuiScreenGamePlay.ActiveGameplayScreen=editor);
                Main.MenuOpen=true;
                InputRouter.Update();
                Controls.Static.BlockUntilRelease();
            }
            catch(Exception ex) { Restore(); Logger.Warning(ex,"Quick toolbar configuration failed"); EssentialHud.Notify("Quick toolbar configuration unavailable"); }
        }
        private static void Closed(MyGuiScreenBase screen,bool unloading) => Restore();
        private static void Restore()
        {
            if(!editing) return;
            editing=false;
            if(editor!=null) editor.Closed-=Closed;
            editor=null;
            if(ReferenceEquals(MyToolbarComponent.CurrentToolbar,store?.Toolbar)) MyToolbarComponent.CurrentToolbar=previous;
            MyToolbarComponent.AutoUpdate=previousAutoUpdate; previous=null;
        }
        public static void Reset()
        {
            editor?.CloseScreen();
            Restore();
            store?.Dispose(); store=null;
            Array.Clear(held,0,held.Length);
            Hide();
        }
        public static void Hide() => ToolbarWheel.Show(null);

        // The pad reports one touch for all four directions, so a direction is selected only while pressed.
        private static void Publish(bool available)
        {
            var controls=Controls.Static;
            int pressed=Array.FindIndex(controls.QuickSlots,b=>b.RawPressed);
            if(!available || pressed<0 && !controls.QuickToolbarTouch.RawPressed) { Hide(); return; }
            ToolbarWheel.Show(View(store.Toolbar,pressed));
        }
        internal static ToolbarWheel.View View(MyToolbar toolbar,int pressed)
        {
            var labels=new string[Slots]; var icons=new string[Slots][]; var enabled=new bool[Slots]; var subIcons=new string[Slots]; var itemText=new string[Slots];
            for(int i=0;i<Slots;i++)
            {
                var item=toolbar.GetItemAtIndex(i);
                labels[i]=item?.DisplayName?.ToString() ?? "Assign";
                icons[i]=item==null ? new[] {GameActions.ConfigureToolbarAction.Icon} : item.Icons==null ? new string[0] : (string[])item.Icons.Clone();
                subIcons[i]=item?.SubIcon; itemText[i]=item?.IconText?.ToString();
                enabled[i]=item==null || item.Enabled;
            }
            return new ToolbarWheel.View { Title="Quick toolbar",Group=1,Page=0,Pages=1,Selected=pressed,Labels=labels,Icons=icons,Enabled=enabled,
                SubIcons=subIcons,ItemText=itemText,Idle="",Footer="Hold: Assign",Compact=true };
        }
    }
}
