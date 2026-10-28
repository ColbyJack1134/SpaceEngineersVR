#if SEVR_DIAGNOSTICS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.Gui;
using Sandbox.Game.World;
using SpaceEngineersVR.Patches;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Plugin;
using VRage.Game;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    // In-world Build Info check: SEVR_BUILDINFO_PROBE=<output folder>, a survival world with Text HUD API and Build Info.
    internal static class BuildInfoProbe
    {
        private static readonly string output=Environment.GetEnvironmentVariable("SEVR_BUILDINFO_PROBE");
        internal static bool Enabled => !string.IsNullOrEmpty(output);
        internal static bool ForceCapture { get; private set; }
        private static readonly string[] blocks={"LargeBlockArmorBlock","LargeBlockLargeGenerator","SmallBlockBatteryBlock"};
        private static int stage,block,edit=-1;
        private static DateTime next=DateTime.MaxValue,deadline;
        private static readonly List<string> failures=new List<string>();
        private static bool rawSeen,rawMissed,done;
        // Render-thread work; false asks for another frame while native images load.
        private static volatile Func<SharpDX.Direct3D11.Device,bool> render;
        private static volatile bool rendered;
        private static readonly MyHudBlockInfo card=new MyHudBlockInfo();

        private static void Require(bool value,string message) { if(!value) { failures.Add(message); Logger.Warning("BUILD INFO PROBE: "+message); } }
        private static void Log(string line) => Logger.Info("BUILD INFO PROBE: "+line);

        // Stages poll their conditions instead of waiting fixed times; settled marks when the text box last changed.
        private const double Poll=.25,Settle=1,BlockLimit=15;
        private static DateTime stageStart,settled;
        internal static void Update()
        {
            if(!Enabled || done) return;
            var now=DateTime.UtcNow;
            if(next==DateTime.MaxValue) { next=now; deadline=now.AddMinutes(4); Log("waiting for the world, Text HUD API and Build Info"); }
            if(now>deadline) { Require(false,"timed out in stage "+stage); Finish(); return; }
            if(stage==1 || stage==2) KeepCard();
            if(stage==31 || stage==32) KeepInstrumentCard();
            if(stage>0 && Sandbox.MySandboxGame.IsPaused) Sandbox.MySandboxGame.PausePop();
            if(now<next) return;
            next=now.AddSeconds(Poll);
            try { Step(now); }
            catch(Exception ex) { Require(false,"stage "+stage+" threw "+ex); Finish(); }
        }
        private static void Enter(int value,DateTime now) { stage=value; stageStart=settled=now; revision=BuildInfoPanel.Text.Revision; }
        private static void Step(DateTime now)
        {
            switch(stage)
            {
                case 0:
                    if(MySession.Static?.LocalCharacter==null || !BuildInfoBridge.Active || !(BuildInfoBridge.HalfHeight()>0)) return;
                    // Single player pauses while the test window lacks focus, which stops mod updates.
                    if(Sandbox.MySandboxGame.IsPaused) Sandbox.MySandboxGame.PausePop();
                    Require(MySession.Static.SurvivalMode,"the probe world must be survival");
                    CheckAdapter();
                    if(Environment.GetEnvironmentVariable("SEVR_BUILDINFO_MULTITOOL")=="1") { Activate("BuildInfo_MultiTool_Large"); Enter(30,now); return; }
                    ForceCapture=true; block=0; Activate(blocks[0]);
                    Enter(1,now);
                    return;
                case 1:
                    // Wait for Build Info's text and the card, then for one second without a text change.
                    if(BuildInfoPanel.Text.Revision!=revision) { revision=BuildInfoPanel.Text.Revision; settled=now; }
                    var view=Current();
                    bool ready=BuildInfoBridge.CubeBuilder && view!=null && view.Sprites.Length>20 && NativeBlockInfo.Captured==cardKey;
                    if((!ready || (now-settled).TotalSeconds<Settle) && (now-stageStart).TotalSeconds<BlockLimit) return;
                    var character=MySession.Static.LocalCharacter;
                    Require(BuildInfoBridge.CubeBuilder,"Build Info did not see the cube builder for "+blocks[block]+" (controlled "+MySession.Static.ControlledEntity?.GetType().Name+
                        ", tool "+character?.EquippedTool?.GetType().Name+", builder "+MyCubeBuilder.Static?.IsActivated+"/"+MyCubeBuilder.Static?.BlockCreationIsActivated+
                        ", bridge "+BuildInfoBridge.Describe()+", cursor "+Sandbox.ModAPI.MyAPIGateway.Gui.IsCursorVisible+", screens "+
                        string.Join(",",Sandbox.Graphics.GUI.MyScreenManager.Screens.Select(x=>x.GetType().Name))+")");
                    Require(view!=null && view.Sprites.Length>20,"no Build Info text captured for "+blocks[block]+" ("+(view?.Sprites.Length ?? 0)+" quads)");
                    // Plane-relative capture: unchanged text keeps its revision while the camera moves.
                    Require((now-settled).TotalSeconds>=Settle,"panel revision kept changing with unchanged text for "+blocks[block]);
                    if(view!=null) Log(blocks[block]+": "+view.Sprites.Length+" quads, "+(view.Width*1000).ToString("0")+" x "+(view.Height*1000).ToString("0")+" mm");
                    if(block==0) CheckDistance();
                    var marks=CardAdditions.Current;
                    if(marks!=null)
                    {
                        var mapped=CardAdditions.Map(marks,1296,1000);
                        var raw=marks.Sprites.SelectMany(m=>new[] {m.TopLeft,m.BottomRight});
                        var card2=mapped.SelectMany(m=>new[] {m.TopLeft,m.BottomRight});
                        Log(blocks[block]+" panel marks: "+marks.Sprites.Length+" block "+marks.Block+" comps "+marks.Components+" card comps "+card.Components.Count+" key "+cardKey+
                            " anchor "+marks.Anchor+" aspect "+marks.Aspect.ToString("0.000")+" raw x "+raw.Min(p=>p.X).ToString("0.000")+".."+raw.Max(p=>p.X).ToString("0.000")+
                            " y "+raw.Min(p=>p.Y).ToString("0.000")+".."+raw.Max(p=>p.Y).ToString("0.000")+" card x "+card2.Min(p=>p.X).ToString("0.000")+".."+card2.Max(p=>p.X).ToString("0.000")+
                            " y "+card2.Min(p=>p.Y).ToString("0.000")+".."+card2.Max(p=>p.Y).ToString("0.000"));
                    }
                    RequestPanel(blocks[block]);
                    Enter(2,now);
                    return;
                case 30:
                    if(!BuildInfoBridge.MultiToolEquipped && (now-stageStart).TotalSeconds<BlockLimit) return;
                    Require(BuildInfoBridge.MultiToolEquipped,"multitool not equipped");
                    if(!BuildInfoBridge.MultiToolEquipped) { Finish(); return; }
                    ForceCapture=true;
                    Enter(31,now);
                    return;
                case 31:
                    if((now-stageStart).TotalSeconds<1) return;
                    Snapshot("switch "+switches+(basic ? " basic":""));
                    RequestPanel("multitool-"+(basic ? "basic-":"")+switches);
                    Enter(32,now);
                    return;
                case 32:
                    if(!rendered) return;
                    if(switches==4)
                    {
                        if(basic) { Finish(); return; }
                        basic=true; switches=0; hudState=Sandbox.MySandboxGame.Config.HudState;
                        Log("HUD state now "+BuildInfoBridge.SetHudState(2));
                        Enter(31,now);
                        return;
                    }
                    GameActions.Execute(BuildInfoBridge.Actions()[0]);
                    switches++;
                    Enter(31,now);
                    return;
                case 2:
                    if(!rendered) return;
                    if(++block<blocks.Length) { Activate(blocks[block]); Enter(1,now); return; }
                    CheckActions();
                    Enter(3,now);
                    return;
                case 3:
                    if(!rendered) return;
                    Require(rawSeen && !rawMissed,"Build Info placement input ran outside the raw distance scope (seen="+rawSeen+", missed="+rawMissed+")");
                    MyCubeBuilder.Static?.Deactivate();
                    MySession.Static.LocalCharacter.SwitchToWeapon((VRage.Game.MyDefinitionId?)null);
                    Inspected=MyEntities.GetEntities().OfType<MyCubeGrid>().SelectMany(g=>g.GetBlocks()).FirstOrDefault(b=>b.FatBlock is Sandbox.Game.Entities.MyCockpit);
                    Require(Inspected!=null,"no block to inspect");
                    if(Inspected!=null) MySlimBlock.SetBlockComponents(card,Inspected);
                    Enter(13,now);
                    return;
                case 13:
                    // Empty hand: the grip card's block becomes Build Info's target.
                    KeepInspectedCard();
                    if(BuildInfoPanel.Text.Revision!=revision) { revision=BuildInfoPanel.Text.Revision; settled=now; }
                    var aimed=Current();
                    if((aimed==null || (now-settled).TotalSeconds<Settle) && (now-stageStart).TotalSeconds<BlockLimit) return;
                    Require(!BuildInfoBridge.CubeBuilder && !BuildInfoBridge.BuildTool,"a tool is still equipped for the empty-hand check");
                    Require(aimed!=null && aimed.Sprites.Length>20,"no Build Info text for an empty-hand inspection ("+(aimed?.Sprites.Length ?? 0)+" quads)");
                    if(aimed!=null) { Log("empty-hand inspection: "+aimed.Sprites.Length+" quads"); RequestPanel("inspected"); } else rendered=true;
                    Enter(14,now);
                    return;
                case 14:
                    KeepInspectedCard();
                    if(!rendered) return;
                    Inspected=null;
                    Enter(15,now);
                    return;
                case 15:
                    if(Current()!=null && (now-stageStart).TotalSeconds<3) return;
                    Require(Current()==null,"Build Info text stayed after the card closed");
                    ForceCapture=false;
                    cockpit=MyEntities.GetEntities().OfType<MyCubeGrid>().SelectMany(g=>g.GetFatBlocks<MyCockpit>()).Where(c=>c.Pilot==null && c.Toolbar!=null)
                        .OrderByDescending(c=>Enumerable.Range(0,c.Toolbar.SlotCount).Count(i=>c.Toolbar.GetItemAtSlot(i)!=null)).FirstOrDefault();
                    Require(cockpit!=null,"no free cockpit in the probe world");
                    if(cockpit==null) { Finish(); return; }
                    cockpit.RequestUse(VRage.Game.Entity.UseObject.UseActionEnum.Manipulate,MySession.Static.LocalCharacter);
                    Enter(5,now);
                    return;
                case 5:
                    bool seated=MySession.Static.ControlledEntity==cockpit;
                    if(!seated && (now-stageStart).TotalSeconds<BlockLimit) return;
                    Require(seated,"could not sit in "+cockpit.DisplayNameText);
                    if(!seated) { Finish(); return; }
                    ForceToolbar=true;
                    Log("cockpit: tool fill "+(BuildInfoBridge.ShipToolFill(out float fill,out _) ? fill.ToString("0.00") : "none")+", weapon mode "+(BuildInfoBridge.WeaponMode ?? "none"));
                    Enter(6,now);
                    return;
                case 6:
                    if(BuildInfoPanel.Toolbar.Revision!=revision) { revision=BuildInfoPanel.Toolbar.Revision; settled=now; }
                    var info=BuildInfoPanel.Toolbar.Current;
                    if((info==null || (now-settled).TotalSeconds<Settle) && (now-stageStart).TotalSeconds<BlockLimit) return;
                    Require(info!=null && info.Sprites.Length>20,"no Build Info toolbar info captured ("+(info?.Sprites.Length ?? 0)+" quads)");
                    Require(!BuildInfoBridge.ToolbarShownByTextApi,"Build Info toolbar box still drawn by Text HUD API in VR");
                    if(info!=null) Log("toolbar info: "+info.Sprites.Length+" quads, "+(info.Width*1000).ToString("0")+" x "+(info.Height*1000).ToString("0")+" mm");
                    if(info!=null) RequestToolbar(info); else rendered=true;
                    Enter(7,now);
                    return;
                case 7:
                    if(!rendered) return;
                    ForceToolbar=false;
                    Enter(8,now);
                    return;
                case 8:
                    // Unforced and with no wheel open, the toolbar box is gone.
                    if(BuildInfoPanel.Toolbar.Current!=null && (now-stageStart).TotalSeconds<3) return;
                    Require(BuildInfoPanel.Toolbar.Current==null && !BuildInfoBridge.ToolbarShownByTextApi,"Build Info toolbar box shown without the toolbar wheel");
                    GameActions.Execute(GameActions.ConfigureToolbarAction);
                    Enter(9,now);
                    return;
                case 9:
                    // In the toolbar configuration screen Build Info's list moves right of the toolbar's page arrow.
                    var area=BuildInfoBridge.ToolbarMenu;
                    if((area==null || BuildInfoPanel.Toolbar.Current==null) && (now-stageStart).TotalSeconds<BlockLimit) return;
                    Require(Main.MenuOpen && area!=null && BuildInfoPanel.Toolbar.Current!=null && !BuildInfoBridge.ToolbarShownByTextApi,
                        "Build Info toolbar list not placed on the toolbar configuration menu");
                    if(area!=null) Log("toolbar configuration list area: "+area.Value);
                    RequestMenu("menu-toolbar-config.png");
                    Enter(10,now);
                    return;
                case 10:
                    if(!rendered) return;
                    if(edit<0)
                    {
                        edit=BuildInfoBridge.ToolbarHighestSlot;
                        cockpit.Toolbar.SetItemAtIndex(edit,null);
                        return;
                    }
                    if(BuildInfoBridge.ToolbarHighestSlot==edit && (now-stageStart).TotalSeconds<BlockLimit) return;
                    Require(edit>=0 && BuildInfoBridge.ToolbarHighestSlot<edit,"Build Info toolbar list not refreshed after a G menu edit");
                    Log("G menu edit: Build Info highest slot "+edit+" -> "+BuildInfoBridge.ToolbarHighestSlot);
                    foreach(var screen in Sandbox.Graphics.GUI.MyScreenManager.Screens.Where(x=>x is Sandbox.Game.Gui.MyGuiScreenToolbarConfigBase).ToArray()) screen.CloseScreenNow();
                    cockpit.ShowTerminal(); Main.MenuOpen=true;
                    Enter(11,now);
                    return;
                case 11:
                    if((now-stageStart).TotalSeconds<4) return;
                    Log("terminal: "+MenuHudLayer.Count+" mod HUD quads");
                    RequestMenu("menu-terminal.png");
                    Enter(12,now);
                    return;
                case 12:
                    if(!rendered) return;
                    foreach(var screen in Sandbox.Graphics.GUI.MyScreenManager.Screens.Where(x=>x is MyGuiScreenTerminal).ToArray()) screen.CloseScreenNow();
                    cockpit.Use();
                    Finish();
                    return;
            }
        }
        internal static bool ForceToolbar { get; private set; }
        // The menu image as submitted to the headset panel.
        private static void RequestMenu(string name)
        {
            rendered=false; int frames=0;
            render=device =>
            {
                if(++frames<3) return false;
                var image=SpaceEngineersVR.Player.Components.VRGUIManager.MenuImage;
                Require(image!=null,"no menu image for "+name);
                if(image!=null) UiTests.Save(image,Path.Combine(output,name));
                return true;
            };
        }
        private static MyCockpit cockpit;
        // The production toolbar wheel with the cockpit's toolbar, and the captured box beside it as in the headset.
        private static void RequestToolbar(BuildInfoPanel.View info)
        {
            var toolbar=cockpit.Toolbar;
            int count=ToolbarWheel.Slots(toolbar);
            var items=Enumerable.Range(0,count).Select(i=>i<toolbar.SlotCount ? toolbar.GetItemAtSlot(i):null).ToArray();
            foreach(var item in items.Where(i=>i!=null)) Log("slot "+item.DisplayName+" status \""+Escape(item.IconText?.ToString())+"\"");
            var model=new ToolbarWheel.View { Title="Toolbar 1/"+toolbar.PageCount,Hint="",Group=0,Pages=toolbar.PageCount,Page=0,Selected=0,
                Labels=items.Select(i=>i?.DisplayName?.ToString() ?? "Assign slot").ToArray(),Icons=items.Select(i=>i?.Icons ?? new string[0]).ToArray(),
                Enabled=items.Select(i=>i==null || i.Enabled).ToArray(),SubIcons=items.Select(i=>i?.SubIcon).ToArray(),ItemText=items.Select(i=>BuildInfoBridge.StatusText(i?.IconText?.ToString())).ToArray() };
            rendered=false;
            render=device =>
            {
                using(var wheel=new OverlayCanvas("Build Info toolbar wheel",1024,1024,1,false,device))
                {
                    ToolbarWheel.Paint(wheel,model); wheel.Upload();
                    if(Loading()) return false;
                    ToolbarWheel.Paint(wheel,model); wheel.Upload();
                    using(var texture=new SharpDX.Direct3D11.ShaderResourceView(device,wheel.Texture))
                    {
                        const float size=.52f,margin=.01f,density=2600;
                        var pose=ToolbarWheel.BelowRing(MatrixD.Identity,size,info.Height);
                        double left=Math.Min(-size/2,pose.Translation.X-info.Width/2)-margin,right=Math.Max(size/2,pose.Translation.X+info.Width/2)+margin;
                        double top=Math.Max(size/2,pose.Translation.Y+info.Height/2)+margin,bottom=Math.Min(-size/2,pose.Translation.Y-info.Height/2)-margin;
                        var centre=MatrixD.CreateTranslation(-(left+right)/2,-(top+bottom)/2,0);
                        var projection=centre*MatrixD.CreateOrthographic(right-left,top-bottom,-1,1);
                        using(var scene=new OverlayCanvas("Build Info toolbar composite",(int)((right-left)*density),(int)((top-bottom)*density),1,false,device))
                        {
                            scene.Clear(System.Drawing.Color.FromArgb(255,10,12,16)); scene.Upload();
                            var sprites=new List<NativeSprite> {PhysicalSurface.Quad(texture,MatrixD.Identity,new RectangleF(-size/2,size/2,size,size),new Vector4(0,0,1,1),Vector4.One,MatrixD.Identity,projection)};
                            sprites.AddRange(info.Sprites.Select(s=>Remap(s,pose,info.Width,info.Height,projection)));
                            NativeSprites.Draw(scene.Texture,sprites);
                            UiTests.Save(scene.Texture,Path.Combine(output,"toolbar-wheel-build-info.png"));
                        }
                    }
                }
                var panelSize=BuildInfoPanel.TextureSize(info.Width,info.Height);
                using(var panel=new OverlayCanvas("Build Info toolbar panel",panelSize.X,panelSize.Y,1,false,device))
                {
                    panel.ClearTexture(); NativeSprites.Draw(panel.Texture,info.Sprites);
                    UiTests.Save(panel.Texture,Path.Combine(output,"panel-toolbar.png"));
                }
                return true;
            };
        }
        private static string Escape(string text) => text==null ? "" : string.Concat(text.Select(c=>c<32 || c>126 ? "\\u"+((int)c).ToString("x4"):c.ToString()));
        private static int switches;
        private static bool basic;
        private static int? hudState;
        private static void Snapshot(string label)
        {
            string hud=MyHud.BlockInfo.BlockName;
            string instrument=BuildInfoBridge.MultiToolInstrument;
            BuildInfoBridge.RefreshInstrument();
            var help=AccessTools.Field(typeof(NativeBlockInfo),"help").GetValue(null);
            string title=(help==null ? null:AccessTools.Field(help.GetType(),"m_blockNameLabel").GetValue(help) as Sandbox.Graphics.GUI.MyGuiControlLabel)?.TextToDraw?.ToString();
            Log(label+": instrument "+instrument+", native block info "+hud+" -> "+MyHud.BlockInfo.BlockName+", card title "+title);
            Require(MyHud.BlockInfo.BlockName==instrument && string.Equals(title,instrument,StringComparison.OrdinalIgnoreCase),"multitool card does not show the selected instrument at "+label);
        }
        private static void KeepInstrumentCard()
        {
            string instrument=BuildInfoBridge.MultiToolInstrument;
            var definition=MyCubeBuilder.Static?.CubeBuilderState?.CurrentBlockDefinition;
            if(instrument==null || definition==null) return;
            string key=definition.Id+"||"+instrument;
            if(key==cardKey) { NativeBlockInfo.Show(card,cardKey); return; }
            BuildInfoBridge.RefreshInstrument();
            BlockInspection.Instrument(card,MyHud.BlockInfo,definition,instrument);
            NativeBlockInfo.Show(card,cardKey=key);
        }
        private static void Finish()
        {
            done=true; ForceCapture=false;
            if(hudState.HasValue) BuildInfoBridge.SetHudState(hudState.Value);
            Logger.Info(failures.Count==0 ? "BUILD INFO PROBE PASSED" : "BUILD INFO PROBE FAILED: "+string.Join("; ",failures));
        }
        private static BuildInfoPanel.View Current() => BuildInfoPanel.Text.Current;
        private static void Activate(string subtype)
        {
            var definition=MyDefinitionManager.Static.GetAllDefinitions().OfType<MyCubeBlockDefinition>().FirstOrDefault(d=>d.Id.SubtypeName==subtype);
            Require(definition!=null,"missing block "+subtype);
            if(definition==null) return;
            // As a player does: put the block in toolbar slot 1 and select it.
            var toolbar=Sandbox.Game.Screens.Helpers.MyToolbarComponent.CurrentToolbar;
            var item=Sandbox.Game.Screens.Helpers.MyToolbarItemFactory.CreateToolbarItem(Sandbox.Game.Screens.Helpers.MyToolbarItemFactory.ObjectBuilderFromDefinition(definition));
            toolbar.SetItemAtSlot(0,item);
            toolbar.ActivateItemAtSlot(0);
            MySlimBlock.SetBlockComponents(card,definition,MyCubeBuilder.BuildComponent.GetBuilderInventory(MySession.Static.LocalCharacter));
        }
        internal static MySlimBlock Inspected { get; private set; }
        private static void KeepInspectedCard() { if(Inspected!=null) NativeBlockInfo.Show(card,cardKey=Inspected.BlockDefinition.Id+"|inspected"); }
        // Production card key form: definition id, then the block.
        private static string cardKey;
        // BlockInspection hides the card without tracked hands; keep it for the composite.
        private static void KeepCard()
        {
            if(MyCubeBuilder.Static?.CurrentBlockDefinition!=null) NativeBlockInfo.Show(card,cardKey=MyCubeBuilder.Static.CurrentBlockDefinition.Id+"|");
        }

        private static void CheckAdapter()
        {
            var mod=AppDomain.CurrentDomain.GetAssemblies().Reverse().Select(a => { try { return a.IsDynamic ? null:a.GetType("Digi.BuildInfo.Features.PlacementDistance",false); } catch { return null; } }).First(t=>t!=null);
            foreach(var name in new[] {"UpdateInput","EditDefinitions"})
            {
                var method=AccessTools.Method(mod,name);
                var info=Harmony.GetPatchInfo(method);
                Require(info!=null && info.Prefixes.Any(p=>p.owner==Common.Plugin.Harmony.Id) && info.Finalizers.Any(p=>p.owner==Common.Plugin.Harmony.Id),"PlacementDistance."+name+" is not wrapped");
            }
            Common.Plugin.Harmony.Patch(AccessTools.Method(mod,"UpdateInput"),new HarmonyMethod(typeof(BuildInfoProbe),nameof(RawProbe)) {priority=Priority.Last});
            // Every Build Info method that reads and writes the stored distance must run in the raw scope.
            var getter=AccessTools.PropertyGetter(typeof(MyBlockBuilderBase),nameof(MyBlockBuilderBase.IntersectionDistance));
            var setter=AccessTools.PropertySetter(typeof(MyBlockBuilderBase),nameof(MyBlockBuilderBase.IntersectionDistance));
            const BindingFlags all=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly;
            var rewrites=new List<string>();
            foreach(var type in AccessTools.GetTypesFromAssembly(mod.Assembly))
            foreach(var method in type.GetMethods(all))
            {
                MethodBase[] calls;
                try { calls=PatchProcessor.ReadMethodBody(method).Select(i=>i.Value as MethodBase).Where(m=>m!=null).ToArray(); }
                catch { continue; }
                if(calls.Contains(getter) && calls.Contains(setter)) rewrites.Add(type.Name+"."+method.Name);
            }
            Require(rewrites.OrderBy(m=>m).SequenceEqual(new[] {"PlacementDistance.EditDefinitions","PlacementDistance.UpdateInput"}),
                "unguarded Build Info distance rewrites: "+string.Join(", ",rewrites));
            Log("adapter: distance wrapped, rewrite scan "+string.Join(", ",rewrites));
        }
        private static void RawProbe(bool inMenu,bool paused)
        {
            if(inMenu || paused) return;
            if(PlacementControls.EditingDistance && PlacementControls.ModDistanceScope) rawSeen=true; else rawMissed=true;
        }
        private static void CheckDistance()
        {
            var stepMethod=AccessTools.Method(typeof(BuildInfoBridge),"Step");
            // As from Build Info's input prefix, inside the raw scope.
            Action<float> step=factor => { bool previous=PlacementControls.BeginRawDistance(); try { stepMethod.Invoke(null,new object[] {factor}); } finally { PlacementControls.EditingDistance=previous; } };
            var distanceField=AccessTools.Field(typeof(BuildInfoBridge),"distance");
            var maxField=(FieldInfo)AccessTools.Field(typeof(BuildInfoBridge),"maxRange").GetValue(null);
            var swapField=(FieldInfo)AccessTools.Field(typeof(BuildInfoBridge),"swapAt").GetValue(null);
            var distance=distanceField.GetValue(null);
            Require(maxField!=null && swapField!=null && distance!=null,"Build Info distance range fields unbound");
            if(maxField==null || swapField==null || distance==null) return;
            float max=(float)maxField.GetValue(distance);
            Require(max>1 && max<=50,"unexpected Build Info range "+max);
            float min=MyBlockBuilderBase.CubeBuilderDefinition.MinBlockBuildingDistance;
            Func<float> raw=() => { bool previous=PlacementControls.BeginRawDistance(); try { return MyBlockBuilderBase.IntersectionDistance; } finally { PlacementControls.EditingDistance=previous; } };
            MyBlockBuilderBase.IntersectionDistance=5;
            step(1.1f);
            Require(Math.Abs(raw()-5.5f)<1e-3 && Math.Abs(((float?)swapField.GetValue(distance) ?? -1)-5.5f)<1e-3,"further step: "+raw()+" swap "+swapField.GetValue(distance));
            step(1/1.1f);
            Require(Math.Abs(raw()-5)<1e-3,"closer step: "+raw());
            step(1000f);
            Require(Math.Abs(raw()-max)<1e-3 && swapField.GetValue(distance)==null,"far clamp: "+raw()+" of "+max);
            step(1e-6f);
            Require(Math.Abs(raw()-min)<1e-3,"near clamp: "+raw()+" of "+min);
            MyBlockBuilderBase.IntersectionDistance=5;
            Log("distance: steps and clamps "+min+" .. "+max+" m");
        }
        private static int revision;

        private static void CheckActions()
        {
            var actions=BuildInfoBridge.Actions();
            Require(actions.Length==7,"expected 7 Build Info wheel actions, got "+actions.Length);
            if(actions.Length!=7) return;
            foreach(var action in actions) { var label=action.Label; var icon=action.Icon; bool enabled=action.Enabled; Require(!string.IsNullOrEmpty(label) && !string.IsNullOrEmpty(icon),"empty wheel action"); }
            var builder=MyCubeBuilder.Static;
            bool transparent=builder.UseTransparency;
            GameActions.Execute(actions[1]);
            Require(builder.UseTransparency!=transparent && actions[1].Label.EndsWith(builder.UseTransparency ? "on":"off"),"transparency toggle");
            GameActions.Execute(actions[1]);
            Require(builder.UseTransparency==transparent,"transparency restore");
            string overlays=actions[0].Label; var seen=new HashSet<string>();
            for(int i=0;i<4;i++) { GameActions.Execute(actions[0]); seen.Add(actions[0].Label); }
            Require(seen.Count==4 && actions[0].Label==overlays,"overlay cycle: "+string.Join(" / ",seen));
            string text=actions[3].Label; var modes=new List<string>();
            for(int i=0;i<3;i++) { GameActions.Execute(actions[3]); modes.Add(actions[3].Label); }
            Require(actions[3].Label==text && modes.All(m=>!m.Contains("press")),"text mode cycle: "+string.Join(" / ",modes));
            Require(!actions[2].Enabled || !builder.DynamicMode,"freeze offered without a grid target");
            Log("wheel: "+string.Join(" | ",actions.Select(a=>a.Label+(a.Enabled ? "":" (disabled)"))));
            var model=new ToolbarWheel.View { Title="Build Info",Hint=ToolbarWheel.QuickControlsHint(true),Group=1,Pages=6,Page=4,Selected=1,
                Labels=Pad(actions.Select(a=>a.Label)),Icons=actions.Select(a=>new[] {a.Icon}).Concat(Enumerable.Repeat(new string[0],9)).Take(9).ToArray(),
                Enabled=actions.Select(a=>a.Enabled).Concat(Enumerable.Repeat(false,9)).Take(9).ToArray(),SubIcons=new string[9],ItemText=new string[9] };
            rendered=false;
            render=device =>
            {
                using(var wheel=new OverlayCanvas("Build Info wheel",1024,1024,1,false,device))
                {
                    ToolbarWheel.Paint(wheel,model); wheel.Upload();
                    if(Loading()) return false;
                    ToolbarWheel.Paint(wheel,model); wheel.Upload();
                    UiTests.Save(wheel.Texture,Path.Combine(output,"wheel-build-info.png"));
                }
                return true;
            };
        }
        private static string[] Pad(IEnumerable<string> labels) => labels.Concat(Enumerable.Repeat("",9)).Take(9).ToArray();
        private static int attempts;
        private static bool Loading()
        {
            NativeSprites.Poll();
            if(NativeSprites.Pending && ++attempts<300) return true;
            attempts=0;
            return false;
        }

        private static void RequestPanel(string name)
        {
            var view=Current();
            rendered=false;
            if(view==null) { rendered=true; return; }
            render=device =>
            {
                var size=BuildInfoPanel.TextureSize(view.Width,view.Height);
                using(var panel=new OverlayCanvas("Build Info panel",size.X,size.Y,1,false,device))
                {
                    panel.ClearTexture(); NativeSprites.Draw(panel.Texture,view.Sprites);
                    if(Loading()) return false;
                    panel.ClearTexture(); NativeSprites.Draw(panel.Texture,view.Sprites);
                    UiTests.Save(panel.Texture,Path.Combine(output,"panel-"+name+".png"));
                }
                foreach(var dark in new[] {true,false}) Composite(device,view,name,dark);
                return true;
            };
        }
        // The card and panel at their headset proportions, right hand: panel, gap, card, top-aligned.
        private static void Composite(SharpDX.Direct3D11.Device device,BuildInfoPanel.View view,string name,bool dark)
        {
            var pixels=NativeBlockInfo.Pixels(card.Components.Count);
            float cardWidth=BlockInspection.Width,cardHeight=cardWidth*pixels.Y/pixels.X;
            float margin=.01f,width=view.Width+BuildInfoPanel.Gap+cardWidth+2*margin,height=Math.Max(view.Height,cardHeight)+2*margin;
            const float density=4000;
            using(var scene=new OverlayCanvas("Build Info composite",(int)(width*density),(int)(height*density),1,false,device))
            {
                scene.Clear(dark ? System.Drawing.Color.FromArgb(255,10,12,16) : System.Drawing.Color.FromArgb(255,186,196,204));
                scene.Upload();
                // Same placement as in the headset: the card's centre, the panel from BuildInfoPanel.Beside.
                var cardPose=MatrixD.CreateTranslation(width/2-margin-cardWidth/2,height/2-margin-cardHeight/2,0);
                var panelPose=BuildInfoPanel.Beside(cardPose,cardWidth,cardHeight,view.Width,view.Height,-1);
                var projection=MatrixD.CreateOrthographic(width,height,-1,1);
                var sprites=view.Sprites.Select(s => Remap(s,panelPose,view.Width,view.Height,projection)).ToList();
                // The card is painted as in the headset, Build Info's panel marks included.
                var cardView=new SurfaceView {Style=SurfaceStyle.BlockInfo,Text=cardKey,Handle=card.Components.Count,Width=cardWidth,Height=cardHeight};
                var cardSize=PhysicalSurface.TextureSize(cardView);
                using(var cardCanvas=new OverlayCanvas("Build Info card",cardSize.X,cardSize.Y,1,false,device))
                using(var cardTexture=new SharpDX.Direct3D11.ShaderResourceView(device,cardCanvas.Texture))
                {
                    Require(NativeBlockInfo.View!=null && NativeBlockInfo.Captured==cardKey,"no native card capture for "+name);
                    BlockInspection.Paint(cardCanvas,cardView);
                    var full=new RectangleF(-cardWidth/2,cardHeight/2,cardWidth,cardHeight);
                    var face=PhysicalSurface.Quad(cardTexture,cardPose,full,new Vector4(0,0,1,1),Vector4.One,MatrixD.Identity,projection);
                    sprites.Insert(0,face);
                    NativeSprites.Draw(scene.Texture,sprites);
                }
                UiTests.Save(scene.Texture,Path.Combine(output,"beside-card-"+name+(dark ? "-dark":"-light")+".png"));
            }
        }
        private static NativeSprite Remap(NativeSprite s,MatrixD pose,float width,float height,MatrixD projection)
        {
            var transform=pose*projection;
            Vector4 Map(Vector4 clip) => (Vector4)Vector4D.Transform(new Vector4D(clip.X*width/2,clip.Y*height/2,0,1),transform);
            s.TopLeft=Map(s.TopLeft); s.TopRight=Map(s.TopRight); s.BottomLeft=Map(s.BottomLeft); s.BottomRight=Map(s.BottomRight);
            return s;
        }
        // Render thread.
        internal static void Render()
        {
            var action=render;
            if(action==null) return;
            bool finished=true;
            try { finished=action(Wrappers.MyRender11.DeviceInstance); }
            catch(Exception ex) { Require(false,"render threw "+ex); }
            if(finished) { render=null; rendered=true; }
        }
    }
}
#endif
