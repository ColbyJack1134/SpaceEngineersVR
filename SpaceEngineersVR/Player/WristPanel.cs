using System;
using System.Collections.Generic;
using System.Linq;
using VRageMath;
using Sandbox.Game.Screens.Helpers;

namespace SpaceEngineersVR.Player
{
    internal static class WristPanel
    {
        private static int tab,page,category;
        private static bool editing;
        private static string query="";
        internal static bool SeatOpen { get; private set; }
        internal static void OpenSeat() { Show(0); SeatOpen=true; }
        internal static bool HudOpen { get; private set; }
        internal static void OpenHud() { Show(0); HudOpen=true; WristHud.Open(); }
        internal static bool DesktopOpen { get; private set; }
        internal static void OpenDesktop() { StopEditing(); HudOpen=SeatOpen=false; DesktopOpen=true; SpatialUi.Expand(); }
        public static void Reset() { StopEditing(); tab=page=category=0; HudOpen=SeatOpen=DesktopOpen=false; query=""; WristSignals.Reset(); }
        internal static bool Inspecting => tab==3;
        public static void StopEditing() { if(editing) MenuKeyboard.Close(); editing=false; }
        internal static void Show(int selected) { StopEditing(); HudOpen=SeatOpen=DesktopOpen=false; tab=selected; }
        internal static void SetQuery(string text) { query=text; page=0; }
        internal static void Edit()
        {
            var owner=Sandbox.Game.World.MySession.Static?.ControlledEntity;
            MenuKeyboard.Open(query,SetQuery,()=>tab==2 && ReferenceEquals(owner,Sandbox.Game.World.MySession.Static?.ControlledEntity),()=>editing=false);
            editing=MenuKeyboard.Standalone;
        }
        public static SurfaceKey[] Keys(MyToolbar toolbar,bool building,bool seated,bool thirdPerson,bool jetpack,EssentialHud.View status,bool? previewToolbar=null)
        {
            int selected=previewToolbar.HasValue ? (previewToolbar.Value ? 1 : 0) : tab;
            if(DesktopOpen && !previewToolbar.HasValue) return DesktopKeys(SpatialUi.DesktopRevealed,DesktopCapture.DisplayedNumber);
            bool showToolbar=selected==1;
            var keys=new List<SurfaceKey>();
            var tabs=new[] {
                new ActionChoice("Quick",()=>Show(0)),new ActionChoice("Toolbar",()=>Show(1)),
                new ActionChoice("Search",()=>Show(2)),new ActionChoice("Navigation",()=>Show(3)) };
            for(int i=0;i<tabs.Length;i++) keys.Add(new SurfaceKey(tabs[i].Label,.02f+i*.243f,.025f,.231f,.1f) {
                Action=tabs[i],Active=i==selected });
            if(selected==0 && SeatOpen && !previewToolbar.HasValue)
            {
                keys.Add(new SurfaceKey("Back",.02f,.16f,.23f,.13f) { Action=new ActionChoice("Back",()=>Show(0)) });
                SeatPanel.WristKeys(keys,SeatFit.Eligible(SeatFit.Seat),CockpitControls.CanAdjust,CockpitControls.Adjusting);
            }
            else if(selected==3) { WristSignals.Keys(keys); return keys.ToArray(); }
            else if(selected==0 && HudOpen && !previewToolbar.HasValue) WristHud.Keys(keys);
            else if(selected==2) SearchKeys(keys);
            else if(showToolbar)
            {
                for(int i=0;i<9;i++)
                {
                    int slot=i; var item=toolbar?.GetItemAtSlot(i);
                    keys.Add(new SurfaceKey(item?.DisplayName?.ToString() ?? "Assign slot",.02f+i%3*.325f,.16f+i/3*.215f,.31f,.20f) {
                        Icons=item?.Icons ?? new[] {GameActions.ConfigureToolbarAction.Icon},SubIcon=item?.SubIcon,
                        Text=(i+1)+ (string.IsNullOrEmpty(item?.IconText?.ToString()) ? "":"  "+item.IconText),
                        Enabled=toolbar!=null && (item==null || item.Enabled),Active=toolbar?.SelectedSlot==i,
                        Action=new ActionChoice("Toolbar slot",()=> {
                            if(!ReferenceEquals(toolbar,MyToolbarComponent.CurrentToolbar)) return;
                            var result=ToolbarWheel.SelectSlot(toolbar,slot,false,GameActions.AssignToolbarSlot);
                            if(result==ToolbarWheel.SlotResult.Activated) GameActions.Reset();
                            else if(result==ToolbarWheel.SlotResult.Unavailable) EssentialHud.Notify("Action unavailable");
                        }) });
                }
                keys.Add(new SurfaceKey("Previous page",.02f,.83f,.25f,.13f) { Enabled=toolbar!=null,Action=new ActionChoice("Previous page",()=>Page(toolbar,-1)) });
                keys.Add(new SurfaceKey("Assign  "+((toolbar?.CurrentPage ?? 0)+1)+" / "+(toolbar?.PageCount ?? 1),.285f,.83f,.43f,.13f) { Enabled=toolbar!=null,Action=GameActions.ConfigureToolbarAction });
                keys.Add(new SurfaceKey("Next page",.73f,.83f,.25f,.13f) { Enabled=toolbar!=null,Action=new ActionChoice("Next page",()=>Page(toolbar,1)) });
            }
            else
            {
                var actions=new[] {GameActions.BlueprintsAction,GameActions.InventoryAction,GameActions.TerminalAction,
                    seated ? (thirdPerson ? GameActions.Quick[22]:SeatAction):GameActions.Quick[3],
                    GameActions.LightsAction,seated ? GameActions.ParkAction:GameActions.HelmetAction,
                    seated ? GameActions.PowerAction:GameActions.JetpackAction,GameActions.Dampeners,
                    seated ? GameActions.RelativeDampeners:GameActions.DetachBootsAction,GameActions.BroadcastAction,
                    GameActions.Quick.First(a=>a.Label=="Chat"),GameActions.Quick.First(a=>a.Label=="Hotkey keyboard")};
                for(int i=0;i<actions.Length;i++)
                    keys.Add(new SurfaceKey(actions[i].Label=="Hotkey keyboard" ? "Hotkeys":actions[i].Label,.02f+i%4*.245f,.16f+i/4*.22f,.23f,.20f) {
                        Action=actions[i],Icons=new[] {actions[i].Icon},Enabled=actions[i].Enabled,Active=Active(actions[i],status) });
                var utilities=new[] {GameActions.PauseAction,GameActions.HudOptions,GameActions.Options,GameActions.Quick[10]};
                var names=new[] {"Pause","HUD","Settings","Help"};
                for(int i=0;i<utilities.Length;i++)
                    keys.Add(new SurfaceKey(names[i],.02f+i*.245f,.84f,.23f,.12f) {
                        Action=utilities[i],Icons=new[] {utilities[i].Icon},Horizontal=true });
            }
            return keys.ToArray();
        }
        internal static SurfaceKey[] DesktopKeys(bool revealed,int monitor)
        {
            var keys=new List<SurfaceKey> {new SurfaceKey("Back",.025f,.044f,.075f,.133f) {Action=new ActionChoice("Back",()=>DesktopOpen=false),Invisible=!revealed}};
            if(monitor>0)
                keys.Add(new SurfaceKey("Monitor",.90f,.044f,.075f,.133f) {Text=monitor.ToString(),
                    Action=new ActionChoice("Next monitor",DesktopCapture.NextMonitor),Invisible=!revealed});
            keys.Add(new SurfaceKey("Play/pause",0,0,1,1) {Action=new ActionChoice("Play/pause",DesktopCapture.PlayPause),Invisible=true});
            return keys.ToArray();
        }
        private static readonly ActionChoice SeatAction=new ActionChoice("Seat panel",OpenSeat,
            icon:NativeSprites.Hud("AdminMenu"),enabled:()=>SeatFit.Eligible(SeatFit.Seat));
        internal static readonly string[] Categories={"All","Suit","Ship","Build","VR"};
        internal static void SelectCategory(int value) { category=Math.Max(0,Math.Min(Categories.Length-1,value)); page=0; }
        internal static bool InCategory(ActionChoice action,int value)
        {
            if(value==0) return true;
            if(value==1) return action==GameActions.JetpackAction || action==GameActions.HelmetAction || action==GameActions.LightsAction || action==GameActions.BroadcastAction ||
                action==GameActions.DetachBootsAction || action==GameActions.Dampeners || action==GameActions.RelativeDampeners || action==GameActions.Quick[3] || action==GameActions.InventoryAction;
            if(value==2) return action==GameActions.PowerAction || action==GameActions.ParkAction || action==GameActions.Dampeners || action==GameActions.RelativeDampeners ||
                action==GameActions.TerminalAction || action==GameActions.BroadcastAction || action==GameActions.CockpitBuild || action==GameActions.ExitFeed || action==GameActions.ResetFeed || action.Label.StartsWith("Toolbar ");
            if(value==3) return GameActions.Building.Contains(action) || action==GameActions.BlueprintsAction || action==GameActions.CockpitBuild;
            return action.Label.StartsWith("VR:") || action==GameActions.Options || action.Label==GameActions.Options.Label || action==GameActions.HudOptions || action==GameActions.Quick[10] ||
                action==GameActions.RecenterAction || action==GameActions.PlayPosture || action==GameActions.DesktopFloating || action==GameActions.DesktopWrist;
        }
        private static void SearchKeys(List<SurfaceKey> keys)
        {
            keys.Add(new SurfaceKey(query.Length==0 ? "Search actions" : query,.02f,.16f,.73f,.13f) {
                Action=new ActionChoice("Search text",Edit),Active=editing });
            keys.Add(new SurfaceKey("Clear",.775f,.16f,.205f,.13f) {
                Enabled=query.Length>0,Action=new ActionChoice("Clear search",()=> { query=""; page=0; }) });
            for(int i=0;i<Categories.Length;i++)
            {
                int index=i;
                keys.Add(new SurfaceKey(Categories[i],.02f+i*.194f,.315f,.182f,.075f) {
                    Active=i==category,Action=new ActionChoice(Categories[i],()=>SelectCategory(index)) });
            }
            var choices=GameActions.Search(query).Where(a=>InCategory(a,category)).ToArray();
            int pages=Math.Max(1,(choices.Length+3)/4); page=Math.Min(page,pages-1);
            for(int i=0;i<4 && page*4+i<choices.Length;i++)
            {
                var action=choices[page*4+i];
                keys.Add(new SurfaceKey(action.Label,.02f+i%2*.49f,.43f+i/2*.19f,.47f,.17f) {
                    Action=action,SearchResult=true,Icons=new[] {action.Icon},Enabled=action.Enabled,Horizontal=true });
            }
            keys.Add(new SurfaceKey("Previous",.02f,.86f,.25f,.115f) { Enabled=page>0,Action=new ActionChoice("Previous",()=> { page--; }) });
            keys.Add(new SurfaceKey(choices.Length==0 ? "No matches" : (page+1)+" / "+pages,.29f,.86f,.42f,.115f) { Enabled=false });
            keys.Add(new SurfaceKey("Next",.73f,.86f,.25f,.115f) { Enabled=page+1<pages,Action=new ActionChoice("Next",()=> { page++; }) });
        }
        private static void Page(MyToolbar toolbar,int change)
        {
            if(toolbar==null || !ReferenceEquals(toolbar,MyToolbarComponent.CurrentToolbar)) return;
            int pages=System.Math.Max(1,toolbar.PageCount);
            toolbar.SwitchToPage((toolbar.CurrentPage+pages+change)%pages);
        }
        private static bool Active(ActionChoice action,EssentialHud.View s)
        {
            if(s==null) return false;
            if(action==GameActions.RelativeDampeners) return s.AutoDampeners;
            if(action==GameActions.Dampeners) return s.Dampeners;
            if(action==GameActions.JetpackAction) return s.Jetpack;
            if(action==GameActions.HelmetAction) return s.Helmet;
            if(action==GameActions.LightsAction) return s.Flashlight;
            if(action==GameActions.BroadcastAction) return s.Piloting ? s.ShipBroadcasting:s.Broadcasting;
            if(action==GameActions.PowerAction) return s.ShipPower;
            if(action==GameActions.ParkAction) return s.ShipPark;
            return false;
        }
    }
}
