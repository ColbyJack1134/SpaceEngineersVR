using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.World;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Plugin;
using SpaceEngineersVR.Util;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;

namespace SpaceEngineersVR.Patches
{
    // Binds to Build Info (workshop 514062285) when its scripts load. Each part fails closed on its own.
    internal static class BuildInfoBridge
    {
        private static readonly List<MethodBase> patched=new List<MethodBase>();
        private static readonly HashSet<Type> attempted=new HashSet<Type>();
        private static DateTime retry;
        private static object main;
        private static FieldInfo instanceField,maxRange,swapAt;
        private static Func<object,bool> isCubeBuilder,isBuildTool;
        private static Func<object,string> overlayName;
        private static Func<object,int> textShow;
        private static Action<object,int> setTextShow;
        private static object equipment,quickMenu,overlays,config,textShowSetting,commands,distance;
        private static MethodInfo cycleOverlays,transparency,freeze,saveConfig,executeCommand;
        private static ActionChoice[] actions;
        private static Func<double> textApiScale;

        internal static bool Active => main!=null && instanceField?.GetValue(null)==main;
#if SEVR_DIAGNOSTICS
        internal static string Describe() => "main="+(main!=null)+" same="+(instanceField?.GetValue(null)==main)+" equipment="+(equipment!=null)+" cube="+(equipment!=null && isCubeBuilder!=null && isCubeBuilder(equipment))+
            " tick="+ModReflection.Get(main,"Tick")+" biPaused="+ModReflection.Get(main,"IsPaused")+" gamePaused="+Sandbox.MySandboxGame.IsPaused+" registered="+ModReflection.Get(main,"ComponentsRegistered");
#endif
        internal static bool CubeBuilder => Active && isCubeBuilder!=null && isCubeBuilder(equipment);
        internal static bool BuildTool => Active && isBuildTool!=null && isBuildTool(equipment);

        // Each assembly is inspected once; without Build Info a refresh only walks the new ones.
        private static readonly HashSet<Assembly> scanned=new HashSet<Assembly>();
        private static readonly List<Type> candidates=new List<Type>();
        internal static void RefreshRegistrations()
        {
            if(!Main.VrActive || MySession.Static==null || DateTime.UtcNow<retry) return;
            retry=DateTime.UtcNow.AddSeconds(1);
            foreach(var assembly in Util.Util.NewestAssemblies())
            {
                if(!scanned.Add(assembly) || assembly.IsDynamic) continue;
                try
                {
                    var type=assembly.GetType("Digi.BuildInfo.BuildInfoMod",false);
                    if(type!=null) candidates.Add(type);
                    var font=assembly.GetType("UIFun.FontTexture",false);
                    var property=font==null ? null:AccessTools.Property(font,"localscale");
                    if(textApiScale==null && property?.GetMethod!=null && property.PropertyType==typeof(double)) textApiScale=() => (double)property.GetValue(null);
                }
                catch { }
            }
            if(main!=null && Active) return;
            // Build Info sets Instance on world load and clears it on unload, so only this world's copy has one.
            foreach(var type in candidates)
            {
                var field=AccessTools.Field(type,"Instance");
                var instance=field?.GetValue(null);
                if(instance==null) continue;
                if(attempted.Add(type)) Attach(type,field,instance);
                return;
            }
        }
        internal static double HalfHeight()
        {
            try { var scale=textApiScale?.Invoke() ?? 0; if(scale>0) return scale; } catch { }
            var camera=MySector.MainCamera;
            return camera==null ? 0 : .1*Math.Tan(camera.FovWithZoom/2);
        }

        private static void Attach(Type type,FieldInfo field,object instance)
        {
            var assembly=type.Assembly;
            Type Find(string name) => assembly.GetType(name,false);
            main=instance; instanceField=field;
            var parts=new List<string>();
            var distanceType=Find("Digi.BuildInfo.Features.PlacementDistance");
            var update=distanceType==null ? null:AccessTools.Method(distanceType,"UpdateInput",new[] {typeof(bool),typeof(bool),typeof(bool)});
            var edit=distanceType==null ? null:AccessTools.Method(distanceType,"EditDefinitions",Type.EmptyTypes);
            if(Try("placement distance",update!=null && edit!=null,() => {
                Patch(update,nameof(DistanceInput),finalizer:nameof(DistanceEnd));
                Patch(edit,nameof(DistanceBegin),finalizer:nameof(DistanceEnd));
            })) parts.Add("placement distance");
            maxRange=distanceType==null ? null:AccessTools.Field(distanceType,"CurrentMaxRange");
            swapAt=distanceType==null ? null:AccessTools.Field(distanceType,"SwapAtDistance");
            distance=ModReflection.Get(instance,"PlacementDistance");
            if(maxRange?.FieldType!=typeof(float) || swapAt?.FieldType!=typeof(float?) || distance==null) { maxRange=swapAt=null; Logger.Info("Build Info distance range fields not found; VR distance steps reach Build Info only through its own input"); }

            var utils=Find("Digi.BuildInfo.Utilities.Utils");
            var matrix=utils==null ? null:AccessTools.Method(utils,"GetEquippedCenteredMatrix",new[] {typeof(MatrixD).MakeByRefType()});
            if(Try("equipped block position",matrix!=null && matrix.ReturnType==typeof(bool),() => Patch(matrix,postfix:nameof(EquippedMatrix)))) parts.Add("equipped block position");

            var text=Find("Digi.BuildInfo.Features.TextGeneration");
            var draw=text==null ? null:AccessTools.Method(text,"UpdateDraw",Type.EmptyTypes);
            if(Try("text panel",draw!=null,() => Patch(draw,nameof(TextBegin),finalizer:nameof(TextEnd)))) parts.Add("text panel");

            var session=Find("Digi.ComponentLib.BuildInfo_GameSession");
            var sessionDraw=session==null ? null:AccessTools.Method(session,"Draw",Type.EmptyTypes);
            if(Try("HUD placement",sessionDraw!=null,() => Patch(sessionDraw,AccessTools.Method(typeof(ModHud),"Begin"),finalizer:AccessTools.Method(typeof(ModHud),"End")))) parts.Add("HUD placement");
            var batch=AccessTools.Method(typeof(VRageRender.MyRenderProxy),nameof(VRageRender.MyRenderProxy.AddBillboards),new[] {typeof(IEnumerable<VRageRender.MyBillboard>)});
            if(Try("see-through overlays",sessionDraw!=null && batch!=null,() => {
                Patch(sessionDraw,nameof(DrawBegin),finalizer:nameof(DrawEnd));
                Patch(batch,nameof(SeeThroughBatch));
            })) parts.Add("see-through overlays");

            var labels=Find("Digi.BuildInfo.Features.ToolbarInfo.ToolbarLabelRender");
            var compute=labels==null ? null:AccessTools.Method(labels,"ComputeVisible",Type.EmptyTypes);
            var labelsUpdate=labels==null ? null:AccessTools.Method(labels,"UpdateAfterSim",new[] {typeof(int)});
            var labelsRender=labels==null ? null:AccessTools.Method(labels,"UpdateRender",Type.EmptyTypes);
            if(Try("toolbar info",compute!=null && labelsUpdate!=null && labelsRender!=null && sessionDraw!=null && BindToolbar(instance,labels),() => {
                Patch(sessionDraw,postfix:nameof(ToolbarDraw));
                Patch(labelsRender,nameof(SingleList),finalizer:nameof(RestoreStyle));
                Patch(compute,postfix:nameof(ToolbarVisible));
                Patch(labelsUpdate,postfix:nameof(ToolbarHidden));
            })) parts.Add("toolbar info");
            else ReleaseToolbar();
            var screenControls=AccessTools.Method(typeof(Sandbox.Graphics.GUI.MyGuiScreenBase),"DrawControls");
            if(toolbarLabels!=null && Try("toolbar list in the G menu",screenControls!=null,() => Patch(screenControls,nameof(MenuToolbarDraw)))) parts.Add("toolbar list in the G menu");

            var bar=Find("Digi.BuildInfo.Features.ShipToolInventoryBar");
            var barDraw=bar==null ? null:AccessTools.Method(bar,"UpdateDraw",Type.EmptyTypes);
            if(Try("ship tool inventory on the wrist",barDraw!=null && BindToolBar(instance,bar),() => Patch(barDraw,nameof(BarDrawBegin),finalizer:nameof(BarDrawEnd)))) parts.Add("ship tool inventory on the wrist");
            var mode=Find("Digi.BuildInfo.Features.WeaponModeIndicator");
            var modeShow=mode==null ? null:AccessTools.Method(mode,"ShowOrUpdateIcon",Type.EmptyTypes);
            hideIcon=mode==null ? null:AccessTools.Method(mode,"HideIcon",Type.EmptyTypes);
            if(Try("weapon mode on the toolbar wheel",modeShow!=null && hideIcon!=null,() => Patch(modeShow,postfix:nameof(ModeShown)))) parts.Add("weapon mode on the toolbar wheel");
            var turret=Find("Digi.BuildInfo.Features.TurretHUD");
            var turretDraw=turret==null ? null:AccessTools.Method(turret,"DrawHUD",Type.EmptyTypes);
            var turretIndicator=turret==null ? null:AccessTools.Method(turret,"UpdateDraw",Type.EmptyTypes);
            hideHud=turret==null ? null:AccessTools.Method(turret,"HideHUD",Type.EmptyTypes);
            if(Try("turret HUD hidden",turretDraw!=null && turretIndicator?.DeclaringType==turret && hideHud!=null,() => { Patch(turretDraw,nameof(TurretSkip)); Patch(turretIndicator,nameof(TurretIndicatorSkip)); })) parts.Add("turret HUD hidden");

            var additions=Find("Digi.BuildInfo.Features.BlockInfoAdditions");
            var additionsDraw=additions==null ? null:AccessTools.Method(additions,"UpdateDraw",Type.EmptyTypes);
            gameConfig=ModReflection.Get(instance,"GameConfig");
            if(Try("block info additions on the card",additionsDraw!=null && gameConfig!=null,() => Patch(additionsDraw,nameof(AdditionsBegin),finalizer:nameof(AdditionsEnd)))) parts.Add("block info additions on the card");

            if(Try("wheel actions",BindActions(instance),null)) parts.Add("wheel actions");
            AttachMultiTool(instance,Find,parts);
            toolbarMonitor=ModReflection.Get(instance,"ToolbarMonitor");
            var monitorType=toolbarMonitor?.GetType();
            var page=monitorType==null ? null:AccessTools.Property(monitorType,"ToolbarPage");
            setPage=monitorType==null ? null:AccessTools.Method(monitorType,"SetToolbarPage",new[] {typeof(Sandbox.ModAPI.IMyShipController),typeof(int)});
            monitorPage=page?.PropertyType==typeof(int) ? (Func<object,int>)(o=>(int)page.GetValue(o)) : null;
            if(setPage!=null && monitorPage!=null) parts.Add("toolbar page"); else { setPage=null; Logger.Info("Build Info toolbar page tracking not found; its toolbar list follows its own page keys"); }
            Logger.Info("Build Info adapter attached to "+assembly.GetName().Name+": "+(parts.Count>0 ? string.Join(", ",parts):"nothing"));
        }
        private static bool Try(string part,bool found,Action attach)
        {
            if(!found) { Logger.Info("Build Info "+part+" not found; left unchanged in VR"); return false; }
            int start=patched.Count;
            try { attach?.Invoke(); return true; }
            catch(Exception ex) { Rollback(start); Logger.Warning(ex,"Build Info "+part+" adapter unavailable"); return false; }
        }
        private static void Patch(MethodBase method,string prefix=null,string postfix=null,string finalizer=null) =>
            Patch(method,prefix==null ? null:AccessTools.Method(typeof(BuildInfoBridge),prefix),postfix==null ? null:AccessTools.Method(typeof(BuildInfoBridge),postfix),
                finalizer==null ? null:AccessTools.Method(typeof(BuildInfoBridge),finalizer));
        private static void Patch(MethodBase method,MethodInfo prefix,MethodInfo postfix=null,MethodInfo finalizer=null)
        {
            patched.Add(method);
            Common.Plugin.Harmony.Patch(method,prefix==null ? null:new HarmonyMethod(prefix),postfix==null ? null:new HarmonyMethod(postfix),
                finalizer:finalizer==null ? null:new HarmonyMethod(finalizer));
        }
        private static readonly MethodInfo[] Patches=new[] {nameof(DistanceInput),nameof(DistanceBegin),nameof(DistanceEnd),nameof(EquippedMatrix),nameof(TextBegin),nameof(TextEnd),nameof(AdditionsBegin),nameof(AdditionsEnd),
                nameof(ToolbarVisible),nameof(ToolbarHidden),nameof(ToolbarDraw),nameof(SingleList),nameof(RestoreStyle),nameof(MenuToolbarDraw),nameof(BarDrawBegin),nameof(BarDrawEnd),nameof(ModeShown),nameof(TurretSkip),nameof(TurretIndicatorSkip),
                nameof(ControlPressed),nameof(ControlJustPressed),nameof(AimBegin),nameof(AimEnd),nameof(HeadBegin),nameof(HeadEnd),nameof(HandCamera),nameof(ToolDrawBegin),
                nameof(InstrumentDrawBegin),nameof(InstrumentDrawEnd),nameof(DrawBegin),nameof(DrawEnd),nameof(SeeThroughBatch),nameof(LegendBegin),nameof(LegendEnd),
                nameof(LabelScopeBegin),nameof(LabelScopeEnd),nameof(LabelAnchor),nameof(LabelBegin),nameof(LabelEnd),
                nameof(HandView),nameof(CopiesBegin),nameof(CopiesEnd),nameof(GeometryBegin),nameof(GeometryEnd)}
            .Select(name=>AccessTools.Method(typeof(BuildInfoBridge),name)).Concat(new[] {AccessTools.Method(typeof(ModHud),"Begin"),AccessTools.Method(typeof(ModHud),"End")}).ToArray();
        private static void Rollback(int start)
        {
            // A method an earlier part also patched keeps that part's patches.
            foreach(var method in patched.Skip(start).Except(patched.Take(start)).ToArray())
                foreach(var patch in Patches)
                    Common.Plugin.Harmony.Unpatch(method,patch);
            patched.RemoveRange(start,patched.Count-start);
        }
        internal static void Reset()
        {
            ReleaseToolbar();
            fed=null; setBlock=null; closedUi=null; toolbarMonitor=null; setPage=null; monitorPage=null;
            toolBar=null; toolBarShown=null; weaponModeShown=false; hideIcon=hideHud=null; gameConfig=null; CardAdditions.Reset();
            Rollback(0); attempted.Clear(); scanned.Clear(); candidates.Clear(); main=null; instanceField=null; actions=null; textApiScale=null; retry=DateTime.MinValue;
            multiTool=null; isEquipped=null; toolActions=null; instrumentName=null; refreshInstrument=null; aimDepth=headDepth=0; drawing=copyDepth=geometryDepth=0; labelText=null; BuildInfoPanel.Legend.Reset(); BuildInfoLabels.Reset();
            maxRange=swapAt=null; distance=null; toolbarLabels=null; toolbarBackgrounds=null; BuildInfoPanel.Text.Reset(); BuildInfoPanel.Toolbar.Reset();
        }

        // Build Info reads, clamps and writes the stored distance; it must see raw metres, as the native distance handler does.
        private static void DistanceBegin(out int __state)
        {
            __state=(PlacementControls.EditingDistance ? 1:0)|(PlacementControls.ModDistanceScope ? 2:0);
            PlacementControls.EditingDistance=true; PlacementControls.ModDistanceScope=true;
        }
        private static void DistanceInput(bool inMenu,bool paused,out int __state)
        {
            DistanceBegin(out __state);
            if(!inMenu && !paused && Main.VrActive) Step(PlacementControls.ModDistanceStep);
        }
        private static Exception DistanceEnd(Exception __exception,int __state)
        {
            PlacementControls.EditingDistance=(__state&1)!=0; PlacementControls.ModDistanceScope=(__state&2)!=0;
            return __exception;
        }
        // Applies the VR stick step where the native builder does not move the distance; Build Info then clamps it.
        private static void Step(float step)
        {
            if(step==1 || maxRange==null || PlacementControls.NativeMovesDistance || !CubeBuilder) return;
            float max=(float)maxRange.GetValue(distance);
            if(!(max>0)) return;
            float min=MyBlockBuilderBase.CubeBuilderDefinition?.MinBlockBuildingDistance ?? 0;
            float value=MathHelper.Clamp(MyBlockBuilderBase.IntersectionDistance*step,min,max);
            MyBlockBuilderBase.IntersectionDistance=value;
            // Build Info's own move records this for the next block swap.
            swapAt.SetValue(distance,value>=max ? (float?)null : value);
        }
        // With nothing under the preview Build Info places its overlays along the head ray.
        private static void EquippedMatrix(ref MatrixD matrix,bool __result)
        {
            var builder=MyCubeBuilder.Static;
            if(!__result || !Main.VrActive || builder?.DynamicMode!=true || builder.HitInfo.HasValue && Multiplayer.ToolContact.HitEntity(builder.HitInfo.Value)!=null) return;
            if(PlacementControls.TryFreeTarget(out var target)) matrix.Translation=target;
            else if(PlacementControls.TryBuilderPose(out var pose)) matrix.Translation=pose.Translation+Vector3D.Normalize(pose.Forward)*PlacementControls.HandRayDistance();
        }
        private static void TextBegin(out BuildInfoPanel.Capture __state)
        {
            bool keep=BlockInspection.Current!=null;
#if SEVR_DIAGNOSTICS
            keep|=Diagnostics.BuildInfoProbe.ForceCapture;
#endif
            // The multitool's own list and description take the box's place; this text describes only its placeholder block.
            if(Main.VrActive && BuildInfoPanel.Capturing==null && MultiToolEquipped) { __state=BuildInfoPanel.Hidden.Begin(false,1); return; }
            __state=Main.VrActive && BuildInfoPanel.Capturing==null ? BuildInfoPanel.Text.Begin(keep,HalfHeight()) : null;
        }
        private static Exception TextEnd(Exception __exception,BuildInfoPanel.Capture __state)
        {
            if(__state==null) return __exception;
            try { __state.Complete(); }
            catch(Exception ex) { Logger.Warning(ex,"Build Info text capture failed"); BuildInfoPanel.Text.Reset(); }
            finally { __state.Dispose(); }
            return __exception;
        }
        // Build Info anchors its panel marks at a lower bottom edge in the basic HUD.
        private static object gameConfig;
#if SEVR_DIAGNOSTICS
        internal static string SetHudState(int state)
        {
            Sandbox.MySandboxGame.Config.HudState=state;
            AccessTools.Method(gameConfig.GetType(),"UpdateHudState").Invoke(gameConfig,new object[] {false});
            return ModReflection.Get(gameConfig,"HudState")?.ToString();
        }
#endif
        private static void AdditionsBegin(out bool __state)
        {
            __state=Main.VrActive && !CardAdditions.Capturing && BuildInfoPanel.Capturing==null;
            if(__state) CardAdditions.Begin();
        }
        private static Exception AdditionsEnd(Exception __exception,bool __state)
        {
            if(__state) CardAdditions.End(ModReflection.Get(gameConfig,"HudState")?.ToString()=="BASIC" ? .558f : .678f);
            return __exception;
        }

        // Build Info's ship toolbar list is captured here, never drawn by Text HUD API in VR.
        private static object toolbarLabels;
        private static FieldInfo labelsMode,styleMode,inConfig,mustBeVisible,wereVisible;
        private static object[] toolbarBackgrounds;
        private static object toolbarList,toolbarColumn;
        private static MethodInfo drawMessage,drawPackage;
        private static PropertyInfo messageVisible,packageVisible;
        private static bool BindToolbar(object instance,Type labels)
        {
            toolbarLabels=ModReflection.Get(instance,"ToolbarLabelRender");
            labelsMode=AccessTools.Field(labels,"LabelsMode"); styleMode=AccessTools.Field(labels,"StyleMode");
            inConfig=AccessTools.Field(labels,"InToolbarConfig"); mustBeVisible=AccessTools.Field(labels,"MustBeVisible"); wereVisible=AccessTools.Field(labels,"WereVisible");
            return toolbarLabels!=null && labelsMode!=null && styleMode!=null && inConfig?.FieldType==typeof(bool) && mustBeVisible?.FieldType==typeof(bool);
        }
        // Build Info creates the box's messages once Text HUD API answers, after this adapter attaches.
        private static bool ToolbarMessages()
        {
            if(toolbarBackgrounds!=null) return true;
            var backgrounds=(ModReflection.Get(toolbarLabels,"Backgrounds") as System.Collections.IEnumerable)?.Cast<object>().ToArray();
            var list=ModReflection.Get(toolbarLabels,"List"); var column=ModReflection.Get(toolbarLabels,"ListColumn2");
            var background=backgrounds?.FirstOrDefault();
            if(background==null || list==null || column==null) return false;
            drawMessage=AccessTools.Method(background.GetType(),"Draw",Type.EmptyTypes); messageVisible=AccessTools.Property(background.GetType(),"Visible");
            drawPackage=AccessTools.Method(list.GetType(),"Draw",Type.EmptyTypes); packageVisible=AccessTools.Property(list.GetType(),"Visible");
            if(drawMessage==null || messageVisible==null || drawPackage==null || packageVisible==null) { DisableToolbar(null); return false; }
            toolbarList=list; toolbarColumn=column; toolbarBackgrounds=backgrounds;
            return true;
        }
        private static void ToolbarVisible(object __instance,ref bool __result)
        {
            // The toolbar configuration screen keeps Build Info's own decision.
            if(toolbarLabels==null || !Main.VrActive || (bool)inConfig.GetValue(__instance)) return;
            var ship=MySession.Static?.ControlledEntity as MyShipController;
            bool wheel=ToolbarWheel.ShowingToolbar;
#if SEVR_DIAGNOSTICS
            wheel|=Diagnostics.BuildInfoProbe.ForceToolbar;
#endif
            __result=wheel && ship!=null && !ship.BuildingMode && Convert.ToInt32(labelsMode.GetValue(__instance))!=0;
        }
#if SEVR_DIAGNOSTICS
        internal static int ToolbarHighestSlot => ModReflection.Get(toolbarMonitor,"HighestIndexUsed") as int? ?? -2;
        internal static bool ToolbarShownByTextApi => toolbarBackgrounds!=null && toolbarBackgrounds.Any(b=>(bool)messageVisible.GetValue(b)) || toolbarList!=null && (bool)packageVisible.GetValue(toolbarList);
#endif
        // Runs after Build Info sets visibility, so Text HUD API never draws the HUD box in VR.
        private static void ToolbarHidden(object __instance)
        {
            if(!Main.VrActive || toolbarLabels==null || !ToolbarMessages()) return;
            try
            {
                foreach(var background in toolbarBackgrounds) messageVisible.SetValue(background,false);
                packageVisible.SetValue(toolbarList,false);
                packageVisible.SetValue(toolbarColumn,false);
            }
            catch(Exception ex) { DisableToolbar(ex); }
        }
        private static void DisableToolbar(Exception ex)
        {
            Logger.Warning(ex,"Build Info toolbar info adapter disabled; its box keeps Build Info's own placement");
            ReleaseToolbar();
        }
        // Build Info reassigns visibility only when it changes; clearing its record makes it show the box itself again.
        private static void ReleaseToolbar()
        {
            try { if(toolbarLabels!=null && wereVisible?.FieldType==typeof(bool?)) wereVisible.SetValue(toolbarLabels,null); }
            catch(Exception ex) { Logger.Warning(ex,"Build Info toolbar visibility not restored"); }
            toolbarLabels=null; toolbarBackgrounds=null; ToolbarMenu=null; WatchEdits(null); BuildInfoPanel.Toolbar.Reset();
        }
        // Below the block grid and right of the toolbar's page arrow in the toolbar configuration screen, as screen fractions.
        internal static RectangleF? ToolbarMenu;
        private static readonly FieldInfo blocksPanel=AccessTools.Field(typeof(Sandbox.Game.Gui.MyGuiScreenToolbarConfigBase),"m_gridBlocksPanel"),
            toolbarControl=AccessTools.Field(typeof(Sandbox.Game.Gui.MyGuiScreenToolbarConfigBase),"m_toolbarControl");
        private static RectangleF? MenuArea()
        {
            var screen=Sandbox.Graphics.GUI.MyScreenManager.Screens.OfType<Sandbox.Game.Gui.MyGuiScreenToolbarConfigBase>().LastOrDefault();
            var panel=screen==null ? null:blocksPanel?.GetValue(screen) as Sandbox.Graphics.GUI.MyGuiControlBase;
            var toolbar=screen==null ? null:toolbarControl?.GetValue(screen) as Sandbox.Graphics.GUI.MyGuiControlBase;
            var size=screen?.GetSize();
            if(panel==null || toolbar==null || size==null) return null;
            Vector2 Pixel(Vector2 normalized) => Sandbox.Graphics.MyGuiManager.GetScreenCoordinateFromNormalizedCoordinate(normalized);
            var full=Sandbox.Graphics.MyGuiManager.GetFullscreenRectangle();
            const float margin=.004f;
            // The page arrows are separate screen buttons on the toolbar's row.
            var row=toolbar.GetPositionAbsoluteTopLeft();
            float right=screen.Controls.OfType<Sandbox.Graphics.GUI.MyGuiControlButton>().Where(b=>b.Visible && b.GetPositionAbsoluteTopLeft().Y<row.Y+toolbar.Size.Y && b.GetPositionAbsoluteTopLeft().Y+b.Size.Y>row.Y)
                .Select(b=>b.GetPositionAbsoluteTopLeft().X+b.Size.X).DefaultIfEmpty(0).Max();
            right=Math.Max(right,row.X+toolbar.Size.X);
            var topLeft=Pixel(new Vector2(right+3*margin,panel.GetPositionAbsoluteTopLeft().Y+panel.Size.Y+margin));
            var bottomRight=Pixel(screen.GetPosition()+size.Value/2-new Vector2(margin));
            if(full.Width<=0 || full.Height<=0 || bottomRight.X<=topLeft.X || bottomRight.Y<=topLeft.Y) return null;
            return new RectangleF((topLeft.X-full.X)/full.Width,(topLeft.Y-full.Y)/full.Height,(bottomRight.X-topLeft.X)/full.Width,(bottomRight.Y-topLeft.Y)/full.Height);
        }
        // Before the screen's controls: above its dimmed background, under the block panel and popups.
        private static void MenuToolbarDraw(Sandbox.Graphics.GUI.MyGuiScreenBase __instance)
        {
            if(ToolbarMenu==null || !Main.VrActive || !(__instance is Sandbox.Game.Gui.MyGuiScreenToolbarConfigBase)) return;
            try { MenuHudLayer.DrawToolbarList(ToolbarMenu.Value); }
            catch(Exception ex) { DisableToolbar(ex); }
        }
        // Build Info rereads the ship toolbar only when a screen closes; edits in the G menu refresh its list straight away.
        private static FieldInfo closedUi;
        private static Sandbox.Game.Screens.Helpers.MyToolbar edited;
        private static void WatchEdits(Sandbox.Game.Screens.Helpers.MyToolbar toolbar)
        {
            if(ReferenceEquals(toolbar,edited)) return;
            if(edited!=null) edited.ItemChanged-=Edited;
            edited=toolbar;
            if(toolbar!=null) toolbar.ItemChanged+=Edited;
        }
        private static void Edited(Sandbox.Game.Screens.Helpers.MyToolbar toolbar,Sandbox.Game.Screens.Helpers.MyToolbar.IndexArgs index,bool sync)
        {
            try { if(equipment!=null && closedUi!=null) closedUi.SetValue(equipment,true); }
            catch(Exception ex) { closedUi=null; Logger.Warning(ex,"Build Info toolbar refresh after edits disabled"); }
        }
        // One column fits beside the wheel; Build Info's own setting is restored after it builds the list.
        private static void SingleList(object __instance,out object __state)
        {
            __state=null;
            if(toolbarLabels==null || !Main.VrActive) return;
            __state=styleMode.GetValue(__instance);
            styleMode.SetValue(__instance,Enum.ToObject(styleMode.FieldType,0));
        }
        private static Exception RestoreStyle(Exception __exception,object __instance,object __state)
        {
            if(__state!=null) styleMode.SetValue(__instance,__state);
            return __exception;
        }
        private static void ToolbarDraw()
        {
            if(!Main.VrActive || toolbarLabels==null) return;
            try
            {
                if(!(bool)mustBeVisible.GetValue(toolbarLabels) || !ToolbarMessages()) { BuildInfoPanel.Toolbar.Reset(); ToolbarMenu=null; WatchEdits(null); return; }
                ToolbarMenu=(bool)inConfig.GetValue(toolbarLabels) ? MenuArea() : null;
                WatchEdits(ToolbarMenu!=null ? (MySession.Static?.ControlledEntity as MyShipController)?.Toolbar : null);
                using(var capture=BuildInfoPanel.Toolbar.Begin(true,HalfHeight()))
                {
                    foreach(var background in toolbarBackgrounds) drawMessage.Invoke(background,null);
                    drawPackage.Invoke(toolbarList,null);
                    capture.Complete();
                }
            }
            catch(Exception ex) { DisableToolbar(ex); }
        }

        private static Func<object,T> Getter<T>(object target,string name)
        {
            var property=target==null ? null:AccessTools.Property(target.GetType(),name);
            if(property?.GetMethod==null || !typeof(T).IsAssignableFrom(property.PropertyType)) return null;
            return o => (T)property.GetValue(o);
        }
        private static bool BindActions(object instance)
        {
            equipment=ModReflection.Get(instance,"EquipmentMonitor"); closedUi=equipment==null ? null:AccessTools.Field(equipment.GetType(),"closedSomeUI"); if(closedUi?.FieldType!=typeof(bool)) closedUi=null;
            quickMenu=ModReflection.Get(instance,"QuickMenu"); overlays=ModReflection.Get(instance,"Overlays");
            config=ModReflection.Get(instance,"Config"); commands=ModReflection.Get(instance,"ChatCommandHandler");
            textShowSetting=ModReflection.Get(config,"TextShow");
            isCubeBuilder=Getter<bool>(equipment,"IsCubeBuilder"); isBuildTool=Getter<bool>(equipment,"IsBuildTool");
            overlayName=Getter<string>(overlays,"OverlayModeName");
            textShow=Getter<int>(textShowSetting,"Value");
            var set=textShowSetting==null ? null:AccessTools.Method(textShowSetting.GetType(),"SetValue",new[] {typeof(int)});
            setTextShow=set==null ? null:(Action<object,int>)((o,v) => set.Invoke(o,new object[] {v}));
            cycleOverlays=overlays==null ? null:AccessTools.Method(overlays.GetType(),"CycleMode",new[] {typeof(bool)});
            transparency=quickMenu==null ? null:AccessTools.Method(quickMenu.GetType(),"SetPlacementTransparency",new[] {typeof(bool),typeof(bool)});
            freeze=quickMenu==null ? null:AccessTools.Method(quickMenu.GetType(),"SetFreezePlacement",new[] {typeof(bool),typeof(bool)});
            saveConfig=config==null ? null:AccessTools.Method(config.GetType(),"Save",Type.EmptyTypes);
            setBlock=equipment==null ? null:AccessTools.Method(equipment.GetType(),"SetBlock");
            if(setBlock!=null && (setBlock.GetParameters().Length<2 || setBlock.GetParameters()[0].ParameterType!=typeof(MyCubeBlockDefinition) ||
                setBlock.GetParameters()[1].ParameterType!=typeof(IMySlimBlock) || setBlock.GetParameters().Skip(2).Any(p=>!p.IsOptional))) setBlock=null;
            if(setBlock==null) Logger.Info("Build Info aimed block setter not found; its text shows only with a tool or a block in hand");
            var help=ModReflection.Get(commands,"CommandHelp"); var reload=ModReflection.Get(commands,"CommandReloadConfig");
            executeCommand=help==null ? null:AccessTools.Method(help.GetType(),"ExecuteNoArgs",Type.EmptyTypes);
            if(new object[] {isCubeBuilder,isBuildTool,overlayName,textShow,setTextShow,cycleOverlays,transparency,freeze,saveConfig,executeCommand,help,reload}.Any(m=>m==null))
            { isCubeBuilder=isBuildTool=null; return false; }
            actions=new[] {
                new ActionChoice(()=>"Overlays: "+OverlayLabel(overlayName(overlays)),()=>cycleOverlays.Invoke(overlays,new object[] {false}),historyKey:"Build Info overlays",icon:()=>NativeSprites.Hud("SignalMode")),
                new ActionChoice(()=>"Transparency: "+(MyCubeBuilder.Static?.UseTransparency==true ? "on":"off"),
                    ()=>transparency.Invoke(quickMenu,new object[] {MyCubeBuilder.Static.UseTransparency!=true,false}),()=>CubeBuilder && MyCubeBuilder.Static!=null,"Build Info transparency",icon:()=>NativeSprites.Hud("PlacementMode")),
                new ActionChoice(()=>"Freeze placement: "+(MyCubeBuilder.Static?.FreezeGizmo==true ? "on":"off"),
                    ()=>freeze.Invoke(quickMenu,new object[] {MyCubeBuilder.Static.FreezeGizmo!=true,false}),
                    ()=>CubeBuilder && MyCubeBuilder.Static!=null && (MyCubeBuilder.Static.FreezeGizmo || !MyCubeBuilder.Static.DynamicMode),"Build Info freeze",icon:()=>NativeSprites.Hud("Handbrake")),
                new ActionChoice(()=>"Text info: "+TextLabel(textShow(textShowSetting)),CycleText,historyKey:"Build Info text",icon:()=>NativeSprites.Hud("HelpScreen")),
                new ActionChoice(()=>"Pick block",()=>GridSelection.Arm(GridSelection.PickBlock),()=>BlockPicker.Available,"Build Info pick block",icon:()=>NativeSprites.Hud("ColorPicker")),
                new ActionChoice(()=>"Build Info help",()=>executeCommand.Invoke(help,null),historyKey:"Build Info help",opensMenu:true,icon:()=>NativeSprites.Hud("HelpScreen")),
                new ActionChoice(()=>"Reload Build Info config",()=>executeCommand.Invoke(reload,null),historyKey:"Build Info reload",icon:()=>NativeSprites.Hud("ReloadGame")) };
            return true;
        }
        internal static string OverlayLabel(string name) => string.IsNullOrEmpty(name) || name=="OFF" ? "off" : name;
        // Build Info's TextShowMode values; ShowOnPress needs a keyboard bind, so VR cycles past it.
        internal const int TextOff=0,TextAlways=1,TextOnPress=2,TextHints=3;
        internal static string TextLabel(int mode) => mode==TextAlways ? "always" : mode==TextHints ? "HUD hints" : mode==TextOnPress ? "on key press" : "off";
        internal static int NextText(int mode) => mode==TextAlways ? TextHints : mode==TextHints ? TextOff : TextAlways;
        private static void CycleText()
        {
            setTextShow(textShowSetting,NextText(textShow(textShowSetting)));
            saveConfig.Invoke(config,null);
        }
        internal static ActionChoice[] Actions()
        {
            if(actions==null || !(CubeBuilder || BuildTool || MySession.Static?.ControlledEntity is Sandbox.Game.Entities.Character.MyCharacter)) return Array.Empty<ActionChoice>();
            return toolActions!=null && MultiToolEquipped ? toolActions.Concat(actions).ToArray() : actions;
        }

        // The multitool's instruments read keyboard and mouse controls and aim along the camera, which in VR is the head.
        private static object multiTool;
        private static Func<object,bool> isEquipped;
        private static ActionChoice[] toolActions;
        private static Func<string> instrumentName;
        private static Action refreshInstrument;
        internal static string MultiToolInstrument => MultiToolEquipped && instrumentName!=null ? instrumentName() : null;
        // Build Info leaves MyHud.BlockInfo stale on the BASIC HUD state.
        internal static void RefreshInstrument() => refreshInstrument?.Invoke();
        internal static bool MultiToolEquipped => Active && multiTool!=null && isEquipped!=null && isEquipped(multiTool);
        private static void AttachMultiTool(object instance,Func<string,Type> find,List<string> parts)
        {
            var tool=find("Digi.BuildInfo.Features.MultiTool.MultiToolHandler");
            multiTool=ModReflection.Get(instance,"MultiTool");
            isEquipped=Getter<bool>(multiTool,"IsEquipped");
            if(tool==null || multiTool==null || isEquipped==null) { multiTool=null; isEquipped=null; Logger.Info("Build Info multitool not found; left unchanged in VR"); return; }

            var input=find("Digi.Input.InputLib");
            var pressed=input==null ? null:AccessTools.Method(input,"GetGameControlPressed");
            var justPressed=input==null ? null:AccessTools.Method(input,"GetGameControlJustPressed");
            if(Try("multitool controls",pressed?.ReturnType==typeof(bool) && justPressed?.ReturnType==typeof(bool) &&
                pressed.GetParameters().Any(p=>p.Name=="controlId" && p.ParameterType==typeof(MyStringId)) && justPressed.GetParameters().Any(p=>p.Name=="controlId" && p.ParameterType==typeof(MyStringId)),() => {
                Patch(pressed,postfix:nameof(ControlPressed));
                Patch(justPressed,postfix:nameof(ControlJustPressed));
            })) parts.Add("multitool controls");

            var measure=find("Digi.BuildInfo.Features.MultiTool.Instruments.Measure.Measure");
            var modifiers=find("Digi.BuildInfo.Features.MultiTool.Instruments.Measure.Modifiers");
            var conveyor=find("Digi.BuildInfo.Features.MultiTool.Instruments.ConveyorVis");
            // Mods read the camera through IMyCamera, which MyCamera implements explicitly.
            var map=typeof(VRage.Game.Utils.MyCamera).GetInterfaceMap(typeof(VRage.ModAPI.IMyCamera));
            int slot=Array.IndexOf(map.InterfaceMethods,AccessTools.PropertyGetter(typeof(VRage.ModAPI.IMyCamera),nameof(VRage.ModAPI.IMyCamera.WorldMatrix)));
            var camera=slot<0 ? null:map.TargetMethods[slot];
            int viewSlot=Array.IndexOf(map.InterfaceMethods,AccessTools.PropertyGetter(typeof(VRage.ModAPI.IMyCamera),nameof(VRage.ModAPI.IMyCamera.ViewMatrix)));
            var viewMatrix=viewSlot<0 ? null:map.TargetMethods[viewSlot];
            var snapshot=find("Digi.BuildInfo.Features.MultiTool.Instruments.PhysicsSnapshot");
            var aim=new[] {measure==null ? null:AccessTools.Method(measure,"HandleAim",Type.EmptyTypes),conveyor==null ? null:AccessTools.Method(conveyor,"Update",new[] {typeof(bool)}),
                snapshot==null ? null:AccessTools.Method(snapshot,"CaptureSnapshot",new[] {typeof(bool)}),snapshot==null ? null:AccessTools.Method(snapshot,"RenderSquare",Type.EmptyTypes)};
            var head=new[] {"DrawCursor","DrawMeasurementLine","DrawMeasurementAngle"}.Select(name=>measure==null ? null:AccessTools.Method(measure,name))
                .Concat(new[] {modifiers==null ? null:AccessTools.Method(modifiers,"Apply")}).ToArray();
            if(Try("multitool hand aim",camera?.ReturnType==typeof(MatrixD) && viewMatrix?.ReturnType==typeof(MatrixD) && aim.All(m=>m!=null) && head.All(m=>m!=null),() => {
                Patch(camera,postfix:nameof(HandCamera));
                Patch(viewMatrix,postfix:nameof(HandView));
                foreach(var method in aim) Patch(method,nameof(AimBegin),finalizer:nameof(AimEnd));
                foreach(var method in head) Patch(method,nameof(HeadBegin),finalizer:nameof(HeadEnd));
            })) parts.Add("multitool hand aim");

            var screen=Array.IndexOf(map.InterfaceMethods,AccessTools.Method(typeof(VRage.ModAPI.IMyCamera),nameof(VRage.ModAPI.IMyCamera.WorldToScreen)));
            var worldToScreen=screen<0 ? null:map.TargetMethods[screen];
            var labelled=new[] {"DrawMeasurementLine","DrawMeasurementAngle"}.Select(name=>measure==null ? null:AccessTools.Method(measure,name)).ToArray();
            var package=find("Digi.BuildInfo.Systems.TextAPI+TextPackage");
            var packageDraw=package==null ? null:AccessTools.Method(package,"Draw",Type.EmptyTypes);
            // Label frames start and end with the see-through part's session draw scope.
            if(Try("measurement labels at their points",parts.Contains("see-through overlays") && worldToScreen!=null && packageDraw!=null && labelled.All(m=>m!=null && m.GetParameters().Any(p=>p.Name=="text")),() => {
                foreach(var method in labelled) Patch(method,nameof(LabelScopeBegin),finalizer:nameof(LabelScopeEnd));
                Patch(worldToScreen,postfix:nameof(LabelAnchor));
                Patch(packageDraw,nameof(LabelBegin),finalizer:nameof(LabelEnd));
            })) parts.Add("measurement labels at their points");

            var instrument=find("Digi.BuildInfo.Features.MultiTool.InstrumentBase");
            var toolDraw=AccessTools.Method(tool,"UpdateDraw",Type.EmptyTypes);
            var instrumentDraws=instrument==null ? new MethodInfo[0] : tool.Assembly.GetTypes().Where(t=>instrument.IsAssignableFrom(t))
                .Select(t=>t.GetMethod("Draw",BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly,null,Type.EmptyTypes,null)).Where(m=>m!=null && !m.IsAbstract).ToArray();
            if(Try("multitool list beside the card",toolDraw!=null && instrumentDraws.Length>0,() => {
                Patch(toolDraw,nameof(ToolDrawBegin),finalizer:nameof(TextEnd));
                foreach(var method in instrumentDraws) Patch(method,nameof(InstrumentDrawBegin),finalizer:nameof(InstrumentDrawEnd));
            })) parts.Add("multitool list beside the card");
            var conveyorView=find("Digi.BuildInfo.Features.Overlays.ConveyorNetwork.ConveyorNetworkView");
            var conveyorRender=find("Digi.BuildInfo.Features.Overlays.ConveyorNetwork.ConveyorNetworkRender");
            var viewDraw=conveyorView==null ? null:AccessTools.Method(conveyorView,"UpdateDraw",Type.EmptyTypes);
            var renderDraw=conveyorRender==null ? null:AccessTools.Method(conveyorRender,"Draw",Type.EmptyTypes);
            if(Try("conveyor legend beside the card",viewDraw?.DeclaringType==conveyorView && renderDraw!=null,() => {
                Patch(viewDraw,nameof(LegendBegin),finalizer:nameof(LegendEnd));
                Patch(renderDraw,nameof(InstrumentDrawBegin),finalizer:nameof(InstrumentDrawEnd));
            })) parts.Add("conveyor legend beside the card");
            // These draw only see-through copies, at any distance; the snapshot's viewfinder and frustum are true geometry.
            var copies=new[] {renderDraw,snapshot==null ? null:AccessTools.Method(snapshot,"RenderPixels",Type.EmptyTypes)};
            var geometry=new[] {"RenderSquare","RenderSnapshotFrustum"}.Select(name=>snapshot==null ? null:AccessTools.Method(snapshot,name,Type.EmptyTypes)).ToArray();
            if(Try("multitool overlay depth",copies.All(m=>m!=null) && geometry.All(m=>m!=null),() => {
                foreach(var method in copies) Patch(method,nameof(CopiesBegin),finalizer:nameof(CopiesEnd));
                foreach(var method in geometry) Patch(method,nameof(GeometryBegin),finalizer:nameof(GeometryEnd));
            })) parts.Add("multitool overlay depth");

            var instruments=AccessTools.Field(tool,"Instruments");
            var index=AccessTools.Field(tool,"InstrumentIdx");
            var current=AccessTools.Property(tool,"Instrument");
            var refresh=AccessTools.Field(tool,"ForceRefresh");
            var selected=instrument==null ? null:AccessTools.Method(instrument,"Selected",Type.EmptyTypes);
            var deselected=instrument==null ? null:AccessTools.Method(instrument,"Deselected",Type.EmptyTypes);
            var clear=measure==null ? null:AccessTools.Method(measure,"ClearMeasurements",Type.EmptyTypes);
            var cancel=measure==null ? null:AccessTools.Method(measure,"CancelProgress",Type.EmptyTypes);
            if(new object[] {instruments,index,current?.GetSetMethod(true),refresh,selected,deselected,clear,cancel}.Any(m=>m==null) || index.FieldType!=typeof(int) || refresh.FieldType!=typeof(bool))
            { Logger.Info("Build Info multitool instruments not found; no instrument actions on the wheel"); return; }
            object Current() => current.GetValue(multiTool);
            instrumentName=()=>ModReflection.Get(Current(),"DisplayNameHUD") as string;
            var refreshInfo=AccessTools.Method(tool,"RefreshBlockInfo",Type.EmptyTypes);
            var equippedDef=AccessTools.Field(tool,"EquippedDef");
            // IsEquipped lags the builder; RefreshBlockInfo throws while the current block is not the multitool.
            if(refreshInfo!=null && equippedDef!=null) refreshInstrument=()=>{
                var def=MyCubeBuilder.Static?.CubeBuilderState?.CurrentBlockDefinition;
                if(def!=null && def==equippedDef.GetValue(multiTool)) refreshInfo.Invoke(multiTool,null);
            };
            bool Is(Type type) => type!=null && type.IsInstanceOfType(Current());
            void Next()
            {
                if(!(instruments.GetValue(multiTool) is System.Collections.IList list) || list.Count==0) return;
                int next=((int)index.GetValue(multiTool)+1)%list.Count;
                if(Current() is object previous) deselected.Invoke(previous,null);
                index.SetValue(multiTool,next);
                current.GetSetMethod(true).Invoke(multiTool,new[] {list[next]});
                selected.Invoke(list[next],null);
                refresh.SetValue(multiTool,true);
            }
            toolActions=new[] {
                new ActionChoice(()=>"Instrument: "+(ModReflection.Get(Current(),"DisplayName") as string ?? "none"),Next,historyKey:"Build Info instrument",icon:()=>NativeSprites.Hud("RadialMenu")),
                new ActionChoice(()=>"Cycle ruler",()=>NativeActions.Pulse(Sandbox.Game.MyControlsSpace.RELOAD),()=>Is(measure),"Build Info ruler",icon:()=>NativeSprites.Hud("PlacementMode")),
                new ActionChoice(()=>"Cycle angle snap",()=>NativeActions.Pulse(Sandbox.Game.MyControlsSpace.SYMMETRY_SWITCH),()=>Is(measure),"Build Info angle snap",icon:()=>NativeSprites.Hud("Symmetry")),
                new ActionChoice(()=>"Delete aimed measurement",()=>NativeActions.Pulse(Sandbox.Game.MyControlsSpace.USE_SYMMETRY),()=>Is(measure),"Build Info delete measurement",icon:()=>NativeSprites.Hud("CutGrid")),
                new ActionChoice(()=>"Delete all measurements",()=>{ var m=Current(); clear.Invoke(m,null); cancel.Invoke(m,null); },()=>Is(measure),"Build Info delete measurements",icon:()=>NativeSprites.Hud("CutGrid")),
                new ActionChoice(()=>"Snapshot colors",()=>NativeActions.Pulse(Sandbox.Game.MyControlsSpace.USE_SYMMETRY),()=>Is(snapshot),"Build Info snapshot colors",icon:()=>NativeSprites.Hud("ColorPicker")) };
            parts.Add("multitool wheel actions");
        }
        private static void ControlPressed(MyStringId controlId,ref bool __result)
        { if(!__result && Main.VrActive && MultiToolEquipped) __result=NativeActions.Read(controlId,VRage.Input.MyControlStateType.PRESSED); }
        private static void ControlJustPressed(MyStringId controlId,ref bool __result)
        { if(!__result && Main.VrActive && MultiToolEquipped) __result=NativeActions.Read(controlId,VRage.Input.MyControlStateType.NEW_PRESSED); }
        // Aiming reads the hand ray; drawing inside it keeps the real camera so cursors and labels face the viewer.
        private static int aimDepth,headDepth;
        private static void AimBegin() => aimDepth++;
        private static Exception AimEnd(Exception __exception) { aimDepth--; return __exception; }
        private static void HeadBegin() => headDepth++;
        private static Exception HeadEnd(Exception __exception) { headDepth--; return __exception; }
        private static void HandCamera(ref MatrixD __result)
        {
            if(aimDepth>0 && headDepth==0 && Main.VrActive && PlacementControls.TryBuilderPose(out var pose)) __result=pose;
        }
        private static void HandView(ref MatrixD __result)
        {
            if(aimDepth>0 && headDepth==0 && Main.VrActive && PlacementControls.TryBuilderPose(out var pose)) __result=MatrixD.Invert(pose);
        }
        private static void ToolDrawBegin(out BuildInfoPanel.Capture __state)
        {
            bool keep=BlockInspection.Current!=null;
#if SEVR_DIAGNOSTICS
            keep|=Diagnostics.BuildInfoProbe.ForceCapture;
#endif
            __state=Main.VrActive && BuildInfoPanel.Capturing==null && MultiToolEquipped ? BuildInfoPanel.Text.Begin(keep,HalfHeight()) : null;
        }
        private static void LegendBegin(out BuildInfoPanel.Capture __state) =>
            __state=Main.VrActive && BuildInfoPanel.Capturing==null ? BuildInfoPanel.Legend.Begin(BlockInspection.Current!=null,HalfHeight()) : null;
        private static Exception LegendEnd(Exception __exception,BuildInfoPanel.Capture __state)
        {
            if(__state==null) return __exception;
            try { __state.Complete(); }
            catch(Exception ex) { Logger.Warning(ex,"Build Info conveyor legend capture failed"); BuildInfoPanel.Legend.Reset(); }
            finally { __state.Dispose(); }
            return __exception;
        }
        private static void InstrumentDrawBegin(out BuildInfoPanel.Capture __state) => __state=BuildInfoPanel.Suspend();
        private static Exception InstrumentDrawEnd(Exception __exception,BuildInfoPanel.Capture __state) { BuildInfoPanel.Resume(__state); return __exception; }

        // Build Info draws see-through overlays as copies at 1% of their distance; in stereo they return to their true place, drawn with no depth test.
        private static int drawing;
        private const double CopyRatio=.01,CopyReach=.5,TextApiPlane=.1,PlaneTolerance=.0005;
        private static int copyDepth,geometryDepth;
        private static void CopiesBegin() => copyDepth++;
        private static Exception CopiesEnd(Exception __exception) { copyDepth--; return __exception; }
        private static void GeometryBegin() => geometryDepth++;
        private static Exception GeometryEnd(Exception __exception) { geometryDepth--; return __exception; }
        private static void DrawBegin() { if(drawing++==0) BuildInfoLabels.BeginFrame(); }
        private static Exception DrawEnd(Exception __exception) { if(--drawing==0) BuildInfoLabels.EndFrame(); return __exception; }

        // A measurement places its label with WorldToScreen; that point anchors the label's capture when it is drawn.
        private sealed class Anchor { public Vector3D Point; }
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object,Anchor> anchors=new System.Runtime.CompilerServices.ConditionalWeakTable<object,Anchor>();
        private static object labelText;
        private static void LabelScopeBegin(object text,out object __state) { __state=labelText; labelText=text; }
        private static Exception LabelScopeEnd(Exception __exception,object __state) { labelText=__state; return __exception; }
        private static void LabelAnchor(ref Vector3D __0)
        {
            if(labelText!=null && Main.VrActive) anchors.GetValue(labelText,_=>new Anchor()).Point=__0;
        }
        // HUD text is laid out on the real camera's plane, also when drawn inside an aim scope.
        private static void LabelBegin(object __instance,out BuildInfoPanel.Capture __state)
        {
            __state=null; headDepth++;
            if(Main.VrActive && BuildInfoPanel.Capturing==null && anchors.TryGetValue(__instance,out var anchor)) __state=BuildInfoLabels.Begin(anchor.Point,HalfHeight());
        }
        private static Exception LabelEnd(Exception __exception,BuildInfoPanel.Capture __state)
        {
            headDepth--;
            if(__state==null) return __exception;
            try { __state.Complete(); }
            catch(Exception ex) { Logger.Warning(ex,"Build Info measurement label capture failed"); }
            finally { __state.Dispose(); }
            return __exception;
        }
        private static readonly List<VRageRender.MyBillboard> batchCopy=new List<VRageRender.MyBillboard>();
        private static void SeeThroughBatch(IEnumerable<VRageRender.MyBillboard> billboards)
        {
            if(drawing==0 || !Main.VrActive || billboards==null) return;
            // Build Info's snapshot passes a collection that only supports CopyTo.
            if(billboards is ICollection<VRageRender.MyBillboard> collection)
            {
                var items=new VRageRender.MyBillboard[collection.Count];
                collection.CopyTo(items,0);
                foreach(var billboard in items) SeeThrough(billboard);
                return;
            }
            batchCopy.Clear(); batchCopy.AddRange(billboards);
            foreach(var billboard in batchCopy) SeeThrough(billboard);
            batchCopy.Clear();
        }
        // True when the billboard is placed and must skip HUD placement.
        internal static bool SeeThrough(VRageRender.MyBillboard billboard)
        {
            if(drawing==0 || !Main.VrActive || billboard==null) return false;
            // True geometry near the hand stays where it is, not on the HUD plane.
            if(geometryDepth>0) return true;
            if(billboard.CustomViewProjection!=-1 || billboard.ParentID!=uint.MaxValue) return false;
            var camera=MySector.MainCamera;
            if(camera==null) return false;
            var origin=camera.Position; Vector3D forward=camera.ForwardVector;
            // Point and Line billboards keep only their start in Position0; Position2 holds sizes (Point: size, Line: length and thickness).
            if(billboard.LocalType!=VRageRender.MyBillboard.LocalTypeEnum.Custom)
            {
                if(!Near(billboard.Position0,origin)) return false;
                billboard.Position0=origin+(billboard.Position0-origin)/CopyRatio;
                var sizes=billboard.Position2;
                billboard.Position2=billboard.LocalType==VRageRender.MyBillboard.LocalTypeEnum.Point ? new Vector3D(sizes.X/CopyRatio,sizes.Y,sizes.Z) : new Vector3D(sizes.X/CopyRatio,sizes.Y/CopyRatio,sizes.Z);
                billboard.DistanceSquared=(float)Vector3D.DistanceSquared(billboard.Position0,origin);
                billboard.BlendType=VRageRender.MyBillboard.BlendTypeEnum.AdditiveTop;
                return true;
            }
            bool triangle=billboard is VRageRender.MyTriangleBillboard;
            if(!Near(billboard.Position0,origin) || !Near(billboard.Position1,origin) || !Near(billboard.Position2,origin) || !triangle && !Near(billboard.Position3,origin)) return false;
            // Text HUD API's own boxes lie on its plane in front of the camera and stay HUD; renderers of copies draw none.
            if(copyDepth==0 && OnPlane(billboard.Position0,origin,forward) && OnPlane(billboard.Position1,origin,forward) && OnPlane(billboard.Position2,origin,forward) &&
                (triangle || OnPlane(billboard.Position3,origin,forward))) return false;
            billboard.Position0=origin+(billboard.Position0-origin)/CopyRatio;
            billboard.Position1=origin+(billboard.Position1-origin)/CopyRatio;
            billboard.Position2=origin+(billboard.Position2-origin)/CopyRatio;
            if(!triangle) billboard.Position3=origin+(billboard.Position3-origin)/CopyRatio;
            var center=triangle ? (billboard.Position0+billboard.Position1+billboard.Position2)/3:(billboard.Position0+billboard.Position2)*.5;
            billboard.DistanceSquared=(float)Vector3D.DistanceSquared(center,origin);
            billboard.BlendType=VRageRender.MyBillboard.BlendTypeEnum.AdditiveTop;
            return true;
        }
        private static bool Near(Vector3D point,Vector3D origin) => copyDepth>0 || Vector3D.DistanceSquared(point,origin)<=CopyReach*CopyReach;
        private static bool OnPlane(Vector3D point,Vector3D origin,Vector3D forward) => Math.Abs(Vector3D.Dot(point-origin,forward)-TextApiPlane)<=PlaneTolerance;

        // Build Info writes toolbar status for its own font: private-use icons beside words, a spacer, a tag line and up to three lines.
        private const char Spacer='\ue13f',Bad='\ue104',Alert='\ue105';
        internal static string StatusText(string text) => Active ? Status(text) : text;
        internal static string Status(string text)
        {
            if(string.IsNullOrEmpty(text) || !text.Any(c=>c>='\ue000' && c<='\uf8ff')) return text;
            var lines=new List<string>();
            foreach(var raw in text.Split('\n'))
            {
                var line=new System.Text.StringBuilder();
                foreach(char c in raw) line.Append(c==Bad || c==Alert ? "! " : c>='\ue000' && c<='\uf8ff' ? " " : c.ToString());
                string clean=System.Text.RegularExpressions.Regex.Replace(line.ToString()," +"," ").Trim();
                if(clean.Length==0 || clean=="c" && raw.IndexOf(Spacer)>=0) continue;
                lines.Add(clean);
            }
            return string.Join(" · ",lines);
        }
        // An empty hand has no Build Info target; the block shown on the grip card becomes it, until a tool takes over.
        private static MethodInfo setBlock;
        private static IMySlimBlock fed;
        internal static void Inspect(IMySlimBlock block)
        {
            if(!Active || setBlock==null || ReferenceEquals(block,fed)) return;
            if(CubeBuilder || BuildTool) { fed=null; return; }
            fed=block;
            try
            {
                var args=setBlock.GetParameters().Select(p=>p.DefaultValue).ToArray();
                args[0]=block?.BlockDefinition as MyCubeBlockDefinition; args[1]=block;
                setBlock.Invoke(equipment,args);
            }
            catch(Exception ex) { setBlock=null; fed=null; Logger.Warning(ex,"Build Info empty-hand inspection disabled"); }
        }
        // Build Info follows the ship toolbar page from page keys only; pages turned on the wheel or wrist are passed on here.
        private static object toolbarMonitor;
        private static MethodInfo setPage;
        private static Func<object,int> monitorPage;
        internal static void SyncPage()
        {
            if(!Main.VrActive || setPage==null || !Active || !(MySession.Static?.ControlledEntity is MyShipController ship)) return;
            var bar=Sandbox.Game.Screens.Helpers.MyToolbarComponent.CurrentToolbar;
            if(bar==null || !ReferenceEquals(bar,ship.Toolbar) || monitorPage(toolbarMonitor)==bar.CurrentPage) return;
            setPage.Invoke(toolbarMonitor,new object[] {ship,bar.CurrentPage});
        }
        // Ship tool fill: Build Info computes it and the wrist shows it; its floating bar is dropped in VR.
        private static object toolBar;
        private static Func<object,bool> toolBarShown;
        private static FieldInfo toolBarFill,toolBarIcon;
        private static bool BindToolBar(object instance,Type bar)
        {
            toolBar=ModReflection.Get(instance,"ShipToolInventoryBar"); toolBarShown=Getter<bool>(toolBar,"Shown");
            toolBarFill=AccessTools.Field(bar,"FilledRatio"); toolBarIcon=AccessTools.Field(bar,"BarIcon");
            return toolBar!=null && toolBarShown!=null && toolBarFill?.FieldType==typeof(float) && toolBarIcon?.FieldType==typeof(MyStringId);
        }
        private static void BarDrawBegin(out BuildInfoPanel.Capture __state) => __state=Main.VrActive && BuildInfoPanel.Capturing==null ? BuildInfoPanel.Hidden.Begin(false,1) : null;
        private static Exception BarDrawEnd(Exception __exception,BuildInfoPanel.Capture __state) { __state?.Dispose(); return __exception; }
        internal static bool ShipToolFill(out float level,out string icon)
        {
            level=0; icon=null;
            if(!Main.VrActive || !Active || toolBar==null || !toolBarShown(toolBar)) return false;
            level=MathHelper.Clamp((float)toolBarFill.GetValue(toolBar),0,1);
            icon=VRageRender.MyTransparentMaterials.GetMaterial((MyStringId)toolBarIcon.GetValue(toolBar)).Texture;
            return !string.IsNullOrEmpty(icon);
        }
        // Weapon mode: shown as a word on the equipped toolbar wheel slot wherever Build Info would show its icon.
        private static MethodInfo hideIcon,hideHud;
        private static bool weaponModeShown;
        private static void ModeShown(object __instance)
        {
            if(!Main.VrActive) return;
            try
            {
                var icon=ModReflection.Get(__instance,"UI_IconBg");
                weaponModeShown=icon!=null && AccessTools.Property(icon.GetType(),"Visible")?.GetValue(icon) is bool visible && visible;
                hideIcon.Invoke(__instance,null);
            }
            catch(Exception ex) { weaponModeShown=false; Logger.Warning(ex,"Build Info weapon mode icon not hidden"); }
        }
        internal static string WeaponMode => Main.VrActive && Active && weaponModeShown && MySession.Static?.ControlledEntity is MyCockpit cockpit ? (cockpit.SingleWeaponMode ? "One":"All") : null;
        // The turret camera screen shows the ammo count.
        private static bool TurretSkip(object __instance)
        {
            if(!Main.VrActive) return true;
            try { hideHud.Invoke(__instance,null); } catch(Exception ex) { Logger.Warning(ex,"Build Info turret HUD not hidden"); }
            return false;
        }
        // Its ship direction mark sits in front of the turret camera, which VR shows on a screen while the eyes stay in the seat.
        private static bool TurretIndicatorSkip() => !Main.VrActive;
    }
}
