#if SEVR_DIAGNOSTICS
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Runtime.Serialization;
using HarmonyLib;
using Sandbox.Game.Gui;
using Sandbox.Game.Multiplayer;
using Sandbox.Game.World;
using Sandbox.Graphics.GUI;
using SpaceEngineersVR.Player;
using VRage.Game;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    // Installed large Battery recipe, fed to both the on-screen controls and the VR capture.
    internal static class BlockInfoFixtures
    {
        internal static readonly string[] Scenarios={"placement","projection","welding","welding-missing","damaged","complete","grinding","armor"};
        private const string Builder="Engineer";
        internal static readonly string Output=System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),"SEVRPrototype","Reports","block-info");
        private static readonly string[] names={"Steel Plate","Construction Comp.","Power Cell","Computer","Construction Comp.","Steel Plate"};
        private static readonly string[] icons={"steel_plate_component","construction_components_component","BatteryComponent","computer_component","construction_components_component","steel_plate_component"};
        private static readonly int[] totals={20,10,80,25,20,60},integrity={100,30,50,1,30,100};
        private const float MaxIntegrity=12925;
        internal static MyHudBlockInfo Info(string scenario)
        {
            if(scenario=="armor")
            {
                var armor=new MyHudBlockInfo {BlockName="Light Armor Block",BlockIcons=new[] {@"Textures\GUI\Icons\Cubes\light_armor_cube.dds"},PCUCost=1,GridSize=MyCubeSize.Large,
                    CriticalIntegrity=1900/2500f,MissingComponentIndex=-1,BlockBuiltBy=1};
                foreach(int count in new[] {20,5})
                    armor.Components.Add(new MyHudBlockInfo.ComponentInfo {ComponentName="Steel Plate",Icons=new[] {@"Textures\GUI\Icons\component\steel_plate_component.dds"},TotalCount=count,MountedCount=count});
                armor.BlockIntegrity=1;
                return armor;
            }
            int[] mounted=new int[6],stock=new int[6];
            if(scenario.StartsWith("welding")) { mounted=new[] {20,10,57,0,0,0}; stock=new[] {0,0,23,scenario=="welding" ? 25:0,0,0}; }
            if(scenario=="damaged") mounted=new[] {20,10,31,0,0,0};
            if(scenario=="complete" || scenario=="grinding") mounted=totals.ToArray();
            var info=new MyHudBlockInfo {BlockName="Battery",BlockIcons=new[] {@"Textures\GUI\Icons\Cubes\Battery.dds"},PCUCost=15,GridSize=MyCubeSize.Large,
                CriticalIntegrity=6250/MaxIntegrity,OwnershipIntegrity=6301/MaxIntegrity,CriticalComponentIndex=2,MissingComponentIndex=scenario=="projection" ? 0:scenario=="welding-missing" ? 3:-1,
                ShowComponentLoss=scenario=="grinding",BlockBuiltBy=scenario=="placement" ? 0:1};
            for(int i=0;i<6;i++)
                info.Components.Add(new MyHudBlockInfo.ComponentInfo {ComponentName=names[i],Icons=new[] {@"Textures\GUI\Icons\component\"+icons[i]+".dds"},
                    TotalCount=totals[i],MountedCount=mounted[i],StockpileCount=stock[i],DeconstructDegrades=i==2});
            info.BlockIntegrity=scenario=="projection" ? .01f:mounted.Select((m,i)=>m*integrity[i]).Sum()/MaxIntegrity;
            return info;
        }
        private static MyGuiControlContextHelp help;
        private static MyGuiControlBlockInfo block;
        private static string drawn;
        private static MyHudBlockInfo info;
        // Title-only HUD mode, as MyGuiScreenHudSpace draws it.
        internal static void DrawNative(MyGuiScreenBase screen,string scenario)
        {
            var previous=MySession.Static;
            try
            {
                var session=(MySession)FormatterServices.GetUninitializedObject(typeof(MySession));
                var players=(MyPlayerCollection)FormatterServices.GetUninitializedObject(typeof(MyPlayerCollection));
                var identity=(MyIdentity)FormatterServices.GetUninitializedObject(typeof(MyIdentity));
                AccessTools.Field(typeof(MyIdentity),"m_displayPlatformName").SetValue(identity,Builder);
                var identities=new ConcurrentDictionary<long,MyIdentity>(); identities[1]=identity;
                AccessTools.Field(typeof(MyPlayerCollection),"m_allIdentities").SetValue(players,identities);
                session.Players=players; MySession.Static=session;
                if(drawn!=scenario)
                {
                    if(help!=null) { screen.Controls.Remove(help); screen.Controls.Remove(block); }
                    var style=NativeBlockInfo.Style();
                    // Set by MyGuiScreenHudSpace in game; the probe has no HUD screen.
                    MyGuiControlBlockInfo.ShowComponentProgress=true;
                    MyGuiControlBlockInfo.CriticalIntegrityColor=new Color(115,69,80);
                    MyGuiControlBlockInfo.OwnershipIntegrityColor=new Color(56,67,147);
                    info=Info(scenario);
                    // The loss notification needs a world HUD; the row colours do not.
                    AccessTools.Field(typeof(MyHudBlockInfo),"m_componentLossNotificationAdded").SetValue(info,info.ShowComponentLoss);
                    help=new MyGuiControlContextHelp(style) {BlockInfo=info,ShowJustTitle=true,ShowBuildInfo=true,Position=new Vector2(.7f,.1f),OriginAlign=VRage.Utils.MyGuiDrawAlignEnum.HORISONTAL_RIGHT_AND_VERTICAL_TOP};
                    block=new MyGuiControlBlockInfo(style) {BlockInfo=info,OriginAlign=VRage.Utils.MyGuiDrawAlignEnum.HORISONTAL_RIGHT_AND_VERTICAL_TOP};
                    block.Position=help.Position+new Vector2(0,help.Size.Y+.006f);
                    screen.Controls.Add(help); screen.Controls.Add(block);
                    drawn=scenario;
                    string path=System.IO.Path.Combine(Output,"vr-block-"+scenario+".png");
                    var due=DateTime.UtcNow.AddSeconds(2); bool saved=false;
                    NativeBlockInfo.Saved=texture =>
                    {
                        // End of BlockInspection's 2 s recapture window.
                        if(saved || DateTime.UtcNow<due) return;
                        saved=true;
                        UiTests.Save(texture,path);
                        var pixels=NativeBlockInfo.Pixels(info.Components.Count);
                        var view=new SurfaceView {Style=SurfaceStyle.BlockInfo,Text=scenario,Width=.16f,Height=.16f*pixels.Y/pixels.X};
                        var size=PhysicalSurface.TextureSize(view);
                        using(var card=new OverlayCanvas("block info card",size.X,size.Y,1,false,texture.Device))
                        {
                            BlockInspection.Paint(card,view);
                            UiTests.Save(card.Texture,System.IO.Path.Combine(Output,"vr-card-"+scenario+".png"));
                        }
                    };
                }
                NativeBlockInfo.Show(info,scenario);
                help.Draw(1,1); block.Draw(1,1);
                NativeBlockInfo.Draw();
            }
            finally { MySession.Static=previous; }
        }
    }
}
#endif
