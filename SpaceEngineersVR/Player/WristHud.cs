using System;
using System.Collections.Generic;
using System.Drawing;
using SpaceEngineersVR.Config;
using SpaceEngineersVR.Plugin;

namespace SpaceEngineersVR.Player
{
    internal static class WristHud
    {
        internal sealed class View
        {
            public int Active,Count;
            public string Key => Active+"|"+Count;
        }
        internal static void Open(PluginConfig config=null) => (config ?? Common.Config).InitializeHudProfiles();
        internal static View Current => Snapshot(Common.Config);
        internal static View Snapshot(PluginConfig c) => new View {Active=c.HudProfileIndex,Count=c.HudProfiles.Length};
        private static string Percent(float value) => Math.Round(value*100)+"%";
        internal static void Keys(List<SurfaceKey> keys,PluginConfig config=null)
        {
            var c=config ?? Common.Config; c.InitializeHudProfiles();
            int selected=c.HudProfileIndex; var p=c.HudProfiles[selected];
            Action<Action<HudProfile>> edit=change=>c.EditHudProfile(selected,change);
            for(int i=0;i<5;i++)
            {
                int index=i; bool exists=i<c.HudProfiles.Length;
                Add(keys,exists ? (i+1)+"  "+c.HudProfiles[i].Name:"+ State",.025f+i*.194f,.22f,.18f,.085f,
                    ()=> {if(!exists)c.AddHudProfile(); c.SelectHudProfile(index);},active:exists && i==selected);
            }
            Add(keys,"Vitals",.025f,.38f,.46f,.077f,()=>edit(v=>v.Vitals=!v.Vitals),value:p.Vitals ? "On":"Off");
            Add(keys,"Markers",.025f,.465f,.46f,.077f,()=>edit(v=>v.Markers=(v.Markers+1)%3),value:new[] {"Off","Icons","Detailed"}[p.Markers]);
            Add(keys,"Grouping",.025f,.55f,.46f,.077f,()=>edit(v=>v.Group=!v.Group),value:p.Group ? "On":"Off");
            Step(keys,"Icon size",p.IconScale,.635f,.75f,2.5f,.25f,v=>edit(h=>h.IconScale=v));
            Step(keys,"Text size",p.TextScale,.72f,.75f,1.5f,.125f,v=>edit(h=>h.TextScale=v));
            Add(keys,"GPS / objectives",.515f,0.380f,.46f,.068f,()=>c.ShowGps=!c.ShowGps,value:c.ShowGps ? "On":"Off");
            Add(keys,"Contacts",.515f,0.455f,.46f,.068f,()=>c.ShowContacts=!c.ShowContacts,value:c.ShowContacts ? "On":"Off");
            Add(keys,"Ore / hacking",.515f,0.530f,.46f,.068f,()=>c.ShowResources=!c.ShowResources,value:c.ShowResources ? "On":"Off");
            Add(keys,"Targeting rings",.515f,0.605f,.46f,.068f,()=>c.SignalRings=!c.SignalRings,value:c.SignalRings ? "On":"Off");
            Add(keys,"Wrist arrows",.515f,0.680f,.46f,.068f,()=>c.SignalEdges=!c.SignalEdges,value:c.SignalEdges ? "On":"Off");
            Add(keys,"HUD with visor open",.515f,0.755f,.46f,.068f,()=>c.HudWithVisorOpen=!c.HudWithVisorOpen,value:c.HudWithVisorOpen ? "On":"Off");
            Add(keys,"Ship crosshair",.515f,.83f,.46f,.068f,()=>c.ShipCrosshair=!c.ShipCrosshair,value:c.ShipCrosshair ? "On":"Off");
            Add(keys,"Back",.025f,.91f,.22f,.07f,()=>WristPanel.Show(0));
            Add(keys,"Reset state",.265f,.91f,.22f,.07f,()=>c.ResetHudProfile(selected));
            if(selected==4) Add(keys,"Remove state",.515f,.91f,.25f,.07f,c.RemoveExtraHudProfile);
        }
        private static void Add(List<SurfaceKey> keys,string label,float x,float y,float w,float h,Action action,bool active=false,bool enabled=true,string value=null)
        {keys.Add(new SurfaceKey(label,x,y,w,h) {Action=new ActionChoice(label,action),Active=active,Enabled=enabled,Value=value});}
        private static void Step(List<SurfaceKey> keys,string name,float value,float y,float min,float max,float step,Action<float> set)
        {
            Add(keys,"−",.025f,y,.08f,.077f,()=>set(Math.Max(min,value-step)),enabled:value>min+.001f);
            Add(keys,name,.115f,y,.28f,.077f,null,enabled:false,value:Percent(value));
            Add(keys,"+",.405f,y,.08f,.077f,()=>set(Math.Min(max,value+step)),enabled:value<max-.001f);
        }
        internal static void Paint(OverlayCanvas target,SurfaceView panel)
        {
            target.Clear(Color.FromArgb(255,12,20,28));
            var g=target.Graphics;
            using(var heading=new Font("Segoe UI",27,FontStyle.Bold,GraphicsUnit.Pixel))
            using(var small=new Font("Segoe UI",19,FontStyle.Regular,GraphicsUnit.Pixel))
            using(var muted=new SolidBrush(Color.FromArgb(157,184,199)))
            {
                g.DrawString("HUD",heading,Brushes.White,26,95);
                g.DrawString("THIS STATE",small,muted,26,211);
                g.DrawString("ALL STATES",small,muted,528,211);
            }
            PhysicalSurface.PaintWristKeys(target,panel);
        }
    }
}
