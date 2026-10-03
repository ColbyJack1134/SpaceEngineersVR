using System;
using System.Collections.Generic;
using System.Linq;
using VRageMath;
using Sandbox.Game.Screens.Helpers;

namespace SpaceEngineersVR.Player
{
    internal static class WristPanel
    {
        private static int tab,page;
        private static bool editing;
        private static string query="";
        internal static bool HudOpen { get; private set; }
        internal static void OpenHud() { Show(0); HudOpen=true; WristHud.Open(); }
        public static void Reset() { StopEditing(); tab=page=0; HudOpen=false; query=""; WristSignals.Reset(); }
        internal static bool Inspecting => tab==3;
        public static void StopEditing() { if(editing) MenuKeyboard.Close(); editing=false; }
        internal static void Show(int selected) { StopEditing(); HudOpen=false; tab=selected; }
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
            bool showToolbar=selected==1;
            var keys=new List<SurfaceKey>();
            var tabs=new[] {
                new ActionChoice("Controls",()=>Show(0)),new ActionChoice("Toolbar",()=>Show(1)),
                new ActionChoice("Search",()=>Show(2)),new ActionChoice("Signals",()=>Show(3)) };
            for(int i=0;i<tabs.Length;i++) keys.Add(new SurfaceKey(tabs[i].Label,.02f+i*.243f,.025f,.231f,.1f) {
                Action=tabs[i],Active=i==selected });
            if(selected==3) { WristSignals.Keys(keys); return keys.ToArray(); }
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
                var actions=GameActions.TabletActions(building,seated,thirdPerson,jetpack);
                for(int i=0;i<actions.Length;i++)
                    keys.Add(new SurfaceKey(actions[i].Label,.02f+i%4*.245f,.16f+i/4*.20f,.23f,.185f) {
                        Action=actions[i]==GameActions.HudOptions ? new ActionChoice("HUD",OpenHud):actions[i],Icons=new[] {actions[i].Icon},
                        Enabled=actions[i].Enabled,Active=Active(actions[i],status) });
            }
            return keys.ToArray();
        }
        private static void SearchKeys(List<SurfaceKey> keys)
        {
            keys.Add(new SurfaceKey(query.Length==0 ? "Search actions" : query,.02f,.16f,.73f,.13f) {
                Action=new ActionChoice("Search text",Edit),Active=editing });
            keys.Add(new SurfaceKey("Clear",.775f,.16f,.205f,.13f) {
                Enabled=query.Length>0,Action=new ActionChoice("Clear search",()=> { query=""; page=0; }) });
            var choices=GameActions.Search(query);
            int pages=Math.Max(1,(choices.Length+5)/6); page=Math.Min(page,pages-1);
            for(int i=0;i<6 && page*6+i<choices.Length;i++)
            {
                var action=choices[page*6+i];
                keys.Add(new SurfaceKey(action.Label,.02f+i%2*.49f,.33f+i/2*.17f,.47f,.15f) {
                    Action=action,Icons=new[] {action.Icon},Enabled=action.Enabled });
            }
            keys.Add(new SurfaceKey("Previous",.02f,.86f,.28f,.115f) { Enabled=page>0,Action=new ActionChoice("Previous",()=> { page--; }) });
            keys.Add(new SurfaceKey(choices.Length==0 ? "No matches" : (page+1)+" / "+pages,.32f,.86f,.36f,.115f) { Enabled=false });
            keys.Add(new SurfaceKey("Next",.70f,.86f,.28f,.115f) { Enabled=page+1<pages,Action=new ActionChoice("Next",()=> { page++; }) });
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
