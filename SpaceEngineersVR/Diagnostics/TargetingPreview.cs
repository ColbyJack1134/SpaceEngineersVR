using System;
using System.IO;
using System.Linq;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SpaceEngineersVR.Player;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    public static class TargetingPreview
    {
        public static void Export(string game,string output,Action<string> log)
        {
            Directory.CreateDirectory(output);
            UiTests.Initialize(game,Path.Combine(output,"data"));
            using(var device=new Device(DriverType.Warp,DeviceCreationFlags.BgraSupport))
            using(var wheel=new OverlayCanvas("Targeting wheels",1024,1024,1,false,device))
            {
                foreach(var fixture in new[] {
                    Tuple.Create("regular",GameActions.WheelActions(false,false,false)),
                    Tuple.Create("jetpack",GameActions.WheelActions(false,false,false,true)),
                    Tuple.Create("building",GameActions.BuildingWheelActions(false)),
                    Tuple.Create("symmetry-setup",GameActions.BuildingWheelActions(true)),
                    Tuple.Create("seated",GameActions.WheelActions(false,true,false)),
                    Tuple.Create("seated-third-person",GameActions.WheelActions(false,true,true)),
                    Tuple.Create("cockpit-building",GameActions.CockpitBuildActions()),
                    Tuple.Create("clipboard",GameActions.ClipboardActions()),
                    Tuple.Create("planet-clipboard",GameActions.ClipboardActions(false)),
                    Tuple.Create("remote",GameActions.RemoteWheelActions()),
                    Tuple.Create("spectator",SpectatorView.Actions()),
                    Tuple.Create("recent-searches",new ActionHistory(new[] {"Auto dampeners","Blueprints","Spectator","Recenter"}).Recent(ActionCatalog.Search(""),9))})
                {
                    var pages=fixture.Item1=="recent-searches" ? ToolbarWheel.WithRecents(Array.Empty<ActionChoice[]>(),fixture.Item2) :
                        ToolbarWheel.WithRecents(BlockVariants.Pages(Array.Empty<ActionChoice>(),fixture.Item2),Array.Empty<ActionChoice>());
                    for(int page=0;page<pages.Length;page++)
                    {
                        var actions=pages[page];
                        var model=new ToolbarWheel.View {Title=page==pages.Length-1 ? "Recent searches" : fixture.Item1=="symmetry-setup" ? "Symmetry setup":pages.Length>1 ? "Quick actions "+(page+1)+" / "+pages.Length:"Quick actions",Group=1,Pages=pages.Length,Page=page,Selected=-1,
                            Labels=actions.Select(a=>a?.Label ?? "").ToArray(),Icons=actions.Select(a=>a==null ? Array.Empty<string>():new[] {a.Icon}).ToArray(),
                            Enabled=actions.Select(a=>a!=null).ToArray(),SubIcons=new string[9],ItemText=new string[9],Hint=ToolbarWheel.QuickControlsHint(pages.Length>1)};
                        UiTests.Render(wheel,()=>ToolbarWheel.Paint(wheel,model));
                        UiTests.Save(wheel.Texture,Path.Combine(output,"wheel-"+fixture.Item1+"-"+(page+1)+".png"));
                    }
                }
                foreach(bool left in new[] {false,true}) foreach(bool selected in new[] {false,true})
                {
                    UiTests.Save(MenuHands.PreviewGlove(device,left,0,new Vector3(0,-1,0),selection:true,selected:selected,
                        tipView:new Vector3(.16f,.03f,-.10f)),Path.Combine(output,"selection-"+(left ? "left":"right")+"-"+(selected ? "target":"waiting")+".png"));
                    UiTests.Save(MenuHands.PreviewGlove(device,left,0,new Vector3(0,-1,0),selection:true,selected:selected),
                        Path.Combine(output,"selection-"+(left ? "left":"right")+"-"+(selected ? "target":"waiting")+"-wide.png"));
                }
                UiTests.Save(MenuHands.PreviewGlove(device,true,0,new Vector3(0,-1,0),tablet:1,selection:true),Path.Combine(output,"selection-over-wrist.png"));
            }
            log("PASS production wheel pages and installed-glove selection feedback previews; action availability is a visual fixture");
        }
    }
}
