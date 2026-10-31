using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Sandbox.Game.Screens.Helpers;
using VRage.Game.Entity;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.Gui;
using HarmonyLib;
using Sandbox.Game.GUI.HudViewers;
using SharpDX.Direct3D11;
using SpaceEngineersVR.Plugin;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal static class WorldMarkers
    {
        internal sealed class Marker
        {
            public Vector3D Position;
            public Vector4 Color,FontColor=Vector4.One,GroupFontColor=new Vector4(117/255f,201/255f,241/255f,1);
            public string Font="White";
            public int NativeType,GridBlocks;
            public string Name, Icon, Kind, Relation, Id, Remaining, Description;
            public bool Pinned, Cluster, Encounter;
            public string LockState;
            public float LockProgress;
            public SignalRing Ring;
            public double Distance;
            // Thin-line Symbol "brackets" or "cross", Span in standard icons, LabelSide null (above), "upleft", "upright" or "below".
            public string Symbol, LabelSide;
            public float Span=1;
            public bool AvoidOverlap, EdgeArrow;
            // Markers that follow an entity: its id and the native height above its position.
            public long Entity;
            public float Lift;
            internal Marker At(Vector3D position) { var copy=(Marker)MemberwiseClone(); copy.Position=position; return copy; }
        }
        internal sealed class View
        {
            public readonly Marker[] Markers;
            public readonly DateTime Time;
            public readonly int Generation;
            public MyHudMarkerRender.SignalMode Mode;
            public bool Reveal;
            public NativeLead.View Lead;
            public View(Marker[] markers,DateTime time) { Markers=markers; Time=time; Generation=generation; }
            private View(Marker[] markers,DateTime time,int capturedGeneration) { Markers=markers; Time=time; Generation=capturedGeneration; }
            // A stale capture keeps only the live markers, stamped now, so they still draw.
            internal View With(Marker[] extra,NativeLead.View lead,bool stale) =>
                new View(stale ? extra : Markers.Concat(extra).ToArray(),stale ? DateTime.UtcNow:Time,Generation) {Mode=Mode,Reveal=Reveal,Lead=lead};
            internal View Replace(Marker[] markers) => new View(markers,Time,Generation) {Mode=Mode,Reveal=Reveal,Lead=Lead};
        }
        // The parallel HUD draw usually finishes after commit, a frame old; entity markers take the entity's position at commit.
        internal static View Live(View view)
        {
            if(view==null) return null;
            Marker[] markers=null;
            for(int i=0;i<view.Markers.Length;i++)
            {
                var marker=view.Markers[i];
                if(marker.Entity==0 || !MyEntities.TryGetEntityById(marker.Entity,out MyEntity item) || item.PositionComp==null) continue;
                var live=item.PositionComp.GetPosition()+item.WorldMatrix.Up*marker.Lift;
                if(live==marker.Position) continue;
                if(markers==null) markers=(Marker[])view.Markers.Clone();
                markers[i]=marker.At(live);
            }
            return markers==null ? view:view.Replace(markers);
        }
        // The native HUD stops capturing markers in third person; older captures are not drawn.
        private const double MaxCaptureAge=1;

        // Added on the main thread each frame, so WeaponCore is never read from the parallel HUD draw.
        private static readonly List<Marker> weaponCore=new List<Marker>();
        internal static View WithWeaponCore(View native)
        {
            if(native==null || !WeaponCoreBridge.ApiReady) return native;
            weaponCore.Clear();
            WeaponCoreMarkers.Add(weaponCore);
            bool stale=(DateTime.UtcNow-native.Time).TotalSeconds>MaxCaptureAge;
            var lead=(stale ? null:native.Lead) ?? WeaponCoreMarkers.Lead();
            if(weaponCore.Count==0 && lead==native.Lead) return native;
            return native.With(weaponCore.ToArray(),lead,stale);
        }
        private static readonly FieldInfo points = AccessTools.Field(typeof(MyHudMarkerRender), "m_pointsOfInterest");
        private static readonly Type point = AccessTools.Inner(typeof(MyHudMarkerRender), "PointOfInterest");
        private static readonly PropertyInfo position = AccessTools.Property(point,"WorldPosition"),
            name = AccessTools.Property(point,"Text"), kind = AccessTools.Property(point,"POIType"),
            relationship = AccessTools.Property(point,"Relationship"), always = AccessTools.Property(point,"AlwaysVisible");
        internal static readonly int EntityTypeStart=Convert.ToInt32(Enum.Parse(kind.PropertyType,"UnknownEntity"));
        private static readonly FieldInfo reveal=AccessTools.Field(typeof(MyHudMarkerRender),"m_disableFading");
        private static readonly FieldInfo playerIndicators=AccessTools.Field(typeof(MyHudMarkerRender),"m_playerIndicatorsDict");
        private static readonly Type playerIndicator=AccessTools.Inner(typeof(MyHudMarkerRender),"MyPlayerIndicator");
        private static readonly PropertyInfo indicatorEntity=AccessTools.Property(playerIndicator,"TargetEntity"),
            indicatorPinned=AccessTools.Property(playerIndicator,"IsAlwaysVisible");
        private static readonly FieldInfo indicatorColor=AccessTools.Field(playerIndicator,"m_relationIndicatorColor_toDraw"),
            indicatorRelation=AccessTools.Field(playerIndicator,"m_targetRelation");
        private static readonly PropertyInfo nativeDistance=AccessTools.Property(point,"Distance");
        private static readonly PropertyInfo cluster=AccessTools.Property(point,"AllowsCluster"),
            remaining=AccessTools.Property(point,"ContainerRemainingTime"),entity=AccessTools.Property(point,"Entity");
        private static readonly FieldInfo poiColor=AccessTools.Field(point,"Color");
        private static readonly MethodInfo colors=AccessTools.Method(point,"GetPOIColorAndFontInformation");
        private sealed class ProxyHistory { public Marker[] Markers=new Marker[0]; public DateTime Time; public long Serial; }
        private static readonly ConditionalWeakTable<MyHudMarkerRender,ProxyHistory> proxyHistory=new ConditionalWeakTable<MyHudMarkerRender,ProxyHistory>();
        private sealed class GpsInfo { public string Id,Name,Description; public bool Encounter; public long Entity; }
        private static readonly ConditionalWeakTable<object,GpsInfo> gpsInfo=new ConditionalWeakTable<object,GpsInfo>();
        internal static void CaptureGps(MyHudMarkerRender renderer,MyGps gps)
        {
            if(!Main.VrActive || MyHudMarkerRender.SignalDisplayMode==MyHudMarkerRender.SignalMode.Off) return;
            var list=(IList)points.GetValue(renderer);
            if(list.Count==0) return;
            object poi=list[list.Count-1];
            gpsInfo.Remove(poi);
            gpsInfo.Add(poi,new GpsInfo { Id="gps:"+gps.Hash,Name=string.IsNullOrEmpty(gps.DisplayName) ? gps.Name:gps.DisplayName,
                Description=gps.Description,Encounter=gps.IsGlobalEncounterGPS || gps.IsContainerGPS,
                // Container GPSs (respawn pods, drop containers) follow their entity, but the native POI does not record it.
                Entity=gps.IsContainerGPS ? gps.EntityId:0 });
        }
        private static readonly Action<MyGuiScreenHudSpace,MyHudGpsMarkers> gpsMarkers=Collector<MyHudGpsMarkers>("DrawGpsMarkers"),buttonMarkers=Collector<MyHudGpsMarkers>("DrawButtonPanelMarkers");
        private static readonly Action<MyGuiScreenHudSpace,MyHudOreMarkers> oreMarkers=Collector<MyHudOreMarkers>("DrawOreMarkers");
        private static readonly Action<MyGuiScreenHudSpace,MyHudLargeTurretTargets> turretTargets=Collector<MyHudLargeTurretTargets>("DrawLargeTurretTargets");
        private static readonly Action<MyGuiScreenHudSpace,MyHudHackingMarkers> hackingMarkers=Collector<MyHudHackingMarkers>("DrawHackingMarkers");
        private static readonly Action<MyGuiScreenHudSpace,MyHudWorldBorderChecker> worldBorder=Collector<MyHudWorldBorderChecker>("DrawWorldBorderIndicator");
        // A missing or changed collector after a game update drops only its markers.
        private static Action<MyGuiScreenHudSpace,T> Collector<T>(string name)
        {
            try { return AccessTools.MethodDelegate<Action<MyGuiScreenHudSpace,T>>(AccessTools.Method(typeof(MyGuiScreenHudSpace),name,new[] {typeof(T)})); }
            catch(Exception) { return null; }
        }
        internal static bool CollectorsResolved => gpsMarkers!=null && buttonMarkers!=null && oreMarkers!=null && turretTargets!=null && hackingMarkers!=null && worldBorder!=null;
        internal static readonly FieldInfo HudTexts=AccessTools.Field(typeof(Sandbox.Game.Gui.MyGuiScreenHudBase),"m_texts");
        // Tab skips the native marker collection and draws; rerun them without desktop sprites so VR captures, the list clears and no names stay queued.
        internal static void CaptureHidden(MyGuiScreenHudSpace hud,MyHudMarkerRender renderer)
        {
            if(failed || !MyHud.IsHudMinimal || HelmetHud.NativeSuppressed) return;
            try
            {
                if(MyHud.LocationMarkers.Visible) renderer.DrawLocationMarkers(MyHud.LocationMarkers);
                if(MyHud.GpsMarkers.Visible && Sandbox.Engine.Utils.MyFakes.ENABLE_GPS) gpsMarkers?.Invoke(hud,MyHud.GpsMarkers);
                if(MyHud.ButtonPanelMarkers.Visible) buttonMarkers?.Invoke(hud,MyHud.ButtonPanelMarkers);
                if(MyHud.OreMarkers.Visible) oreMarkers?.Invoke(hud,MyHud.OreMarkers);
                if(MyHud.LargeTurretTargets.Visible) turretTargets?.Invoke(hud,MyHud.LargeTurretTargets);
                worldBorder?.Invoke(hud,MyHud.WorldBorderChecker);
                if(MyHud.HackingMarkers.Visible) hackingMarkers?.Invoke(hud,MyHud.HackingMarkers);
                if(MyHud.OffscreenTargetMarker.Visible) renderer.AddOffscreenTarget(MyHud.OffscreenTargetMarker.Position,MyHud.OffscreenTargetMarker.TargetPlayerRelation);
                RemoteHud.Discarding++;
                try { renderer.DrawTargetIndicatorRender(); renderer.Draw(); }
                finally { RemoteHud.Discarding--; (HudTexts?.GetValue(hud) as VRage.Generics.MyObjectsPoolSimple<Sandbox.Game.Gui.MyHudText>)?.ClearAllAllocated(); }
            }
            catch(Exception ex) { ((IList)points.GetValue(renderer)).Clear(); RenderFrameBridge.CaptureMarkers(null); recovery.Fail(ex,"VR markers with a hidden native HUD disabled"); }
        }
        // The lock ring's progress updates only in the targeting markers' draw, which the game skips while its crosshair is off,
        // as it is with the desktop HUD hidden; run it with its desktop sprites discarded so the VR ring still fills.
        private static bool targetingStatsFailed;
        internal static void UpdateTargetingStats()
        {
            if(failed || targetingStatsFailed || Sandbox.MySandboxGame.Config.ShowCrosshairHUD || HelmetHud.NativeSuppressed || MyHud.TargetingMarkers==null) return;
            RemoteHud.Discarding++;
            try { MyHud.TargetingMarkers.Draw(); }
            catch(Exception ex) { targetingStatsFailed=true; Logger.Warning(ex,"VR lock ring progress with a hidden native crosshair disabled"); }
            finally { RemoteHud.Discarding--; }
        }
        private static View renderSnapshot;
        private static MatrixD renderHead,signalHead;
        private static Vector3D signalUp;
        private static ShipCrosshair.View renderCrosshair;
        internal static MatrixD RenderHead => renderHead;
        internal static View RenderSnapshot => renderSnapshot;
        private static SignalLayout.Options renderOptions=new SignalLayout.Options();
        private static bool markersVisible;
        internal static SignalLayout.Options WristOptions => renderOptions;
        private static SignalLayout.Entry[] layout=new SignalLayout.Entry[0];
        private static int generation;
        internal const int LabelWidth=1024, LabelHeight=96, AtlasWidth=2048, AtlasHeight=3072;
        private static readonly RenderRecovery recovery=new RenderRecovery("VR world markers");
        private static bool failed => recovery.Failed;

        public static void Capture(MyHudMarkerRender renderer)
        {
            if (failed || !Main.VrActive) return;
            try
            {
                var mode=RemoteHud.GlobalSignalMode;
                if (!Main.WorldAvailable) { RenderFrameBridge.CaptureMarkers(null); return; }
                var head=CameraRig.Current?.Anchor.Translation ?? Sandbox.Game.World.MySector.MainCamera.Position;
                var now=DateTime.UtcNow;
                var snapshot=Read(renderer,mode,head,now) ?? new View(new Marker[0],now) {Mode=mode};
                snapshot.Lead=NativeLead.Capture(renderer);
                RenderFrameBridge.CaptureMarkers(snapshot);
            }
            catch (Exception ex) { RenderFrameBridge.CaptureMarkers(null); recovery.Fail(ex,"VR world markers disabled; native desktop markers retained"); }
        }

        internal static View Read(MyHudMarkerRender renderer,MyHudMarkerRender.SignalMode mode,Vector3D head,DateTime now)
        {
            if(mode==MyHudMarkerRender.SignalMode.Off) return null;
            var markers=new List<Marker>();
            // Copy before native grouping mutates and recycles the POIs. Do not retain engine objects.
            foreach (object poi in (IEnumerable)points.GetValue(renderer))
            {
                string type=kind.GetValue(poi).ToString();
                var world=(Vector3D)position.GetValue(poi);
                if (!world.IsValid()) continue;
                string relation=relationship.GetValue(poi).ToString();
                bool gps=type=="GPS" || type=="ContractGPS" || type=="Objective";
                bool pinned=(bool)always.GetValue(poi);
                var args=new object[] { default(VRageMath.Color),default(VRageMath.Color),null,null };
                colors.Invoke(poi,args);
                var tint=(VRageMath.Color)args[0];
                string text=name.GetValue(poi)?.ToString() ?? "";
                var item=entity.GetValue(poi) as MyEntity;
                string id=item!=null ? "entity:"+item.EntityId : type+":"+text+":"+world.ToString();
                GpsInfo info=null;
                if(gps) { gpsInfo.TryGetValue(poi,out info); gpsInfo.Remove(poi); }
                string icon=type=="Scenario" ? "scenario" : gps ? "gps" : relation=="Owner" ? "self" :
                    relation=="Enemies" ? "enemy" : relation=="FactionShare" || relation=="Friends" ? "friendly" : "neutral";
                markers.Add(new Marker { Position=world,Color=tint.ToVector4(),FontColor=((VRageMath.Color)args[1]).ToVector4(),Font=args[2] as string ?? "White",GroupFontColor=((VRageMath.Color)poiColor.GetValue(poi)).ToVector4(),Name=text,
                    NativeType=Convert.ToInt32(kind.GetValue(poi)),GridBlocks=(item as Sandbox.Game.Entities.MyCubeBlock)?.CubeGrid?.BlocksCount ?? 0,
                    Kind=type,Relation=relation,Id=item==null && type=="UnknownEntity" ? null:info?.Id ?? id,Description=info?.Description ?? "",
                    Remaining=remaining.GetValue(poi) as string,Encounter=info?.Encounter==true || type=="Scenario",
                    Pinned=pinned,Cluster=(bool)cluster.GetValue(poi),
                    Icon=type=="Ore" ? "ore" : @"Textures\HUD\marker_"+icon+".dds",
                    Distance=(double)nativeDistance.GetValue(poi),Entity=item?.EntityId ?? info?.Entity ?? 0,Lift=item is MyCharacter ? 1.3f:0 });
            }
            IdentifyProxies(renderer,markers,now);
            ReadTarget(renderer,markers);
            ReadPlayers(renderer,markers,head);
            return new View(markers.ToArray(),now) { Mode=mode,Reveal=(bool)reveal.GetValue(null) };
        }

        private static void IdentifyProxies(MyHudMarkerRender renderer,List<Marker> markers,DateTime now)
        {
            var history=proxyHistory.GetValue(renderer,_=>new ProxyHistory());
            var anonymous=markers.Where(m=>m.Id==null).ToArray();
            double elapsed=(now-history.Time).TotalSeconds;
            if(elapsed>=0 && elapsed<=.5)
            {
                double reach=Math.Max(50,elapsed*5000);
                var pairs=new List<Tuple<double,int,int>>();
                for(int i=0;i<anonymous.Length;i++) for(int j=0;j<history.Markers.Length;j++)
                {
                    var a=anonymous[i]; var b=history.Markers[j];
                    if(a.Name!=b.Name || a.Relation!=b.Relation) continue;
                    double distance=Vector3D.DistanceSquared(a.Position,b.Position);
                    if(distance<=reach*reach) pairs.Add(Tuple.Create(distance,i,j));
                }
                var used=new HashSet<int>();
                foreach(var pair in pairs.OrderBy(p=>p.Item1))
                    if(anonymous[pair.Item2].Id==null && used.Add(pair.Item3)) anonymous[pair.Item2].Id=history.Markers[pair.Item3].Id;
            }
            // Proxy broadcasts have no entity ID. Associate identity only; never filter their positions.
            foreach(var marker in anonymous) if(marker.Id==null) marker.Id="proxy:"+(++history.Serial);
            history.Markers=anonymous; history.Time=now;
        }

        private static void ReadTarget(MyHudMarkerRender renderer,List<Marker> markers)
        {
            var target=renderer.TargetIndicatorRender?.TargetInfo;
            var circle=MyHud.TargetingMarkers?.TargetingCircle;
            if(target?.IsSet!=true || target.Entity==null || target.Entity.Closed || target.Entity.MarkedForClose || circle==null || circle.State.ToString()!="Visible") return;
            if(HelmetHud.NativeSuppressed) return;
            var controlled=Sandbox.Game.World.MySession.Static?.ControlledEntity;
            if(!(controlled is Sandbox.ModAPI.IMyTargetingCapableBlock capable) || !capable.IsTargetLockingEnabled() || capable.IsShipToolSelected()) return;
            if(controlled is Sandbox.Game.Entities.MyCubeBlock block && (!block.IsWorking || !block.CubeGrid.IsPowerSwitchOn)) return;
            if(target.Entity is Sandbox.Game.Entities.MyCubeGrid grid && grid.Physics==null) return;
            var marker=markers.FirstOrDefault(m=>m.Kind=="OffscreenTarget");
            if(marker==null)
            {
                marker=new Marker { Kind="OffscreenTarget",Pinned=true };
                markers.Add(marker);
            }
            marker.Id="lock:"+target.Entity.EntityId;
            marker.Name=markers.FirstOrDefault(m=>m.Id=="entity:"+target.Entity.EntityId)?.Name ?? "";
            marker.LockState=target.State.ToString(); marker.LockProgress=target.ProgressPercent; marker.Ring=SignalRing.Read(circle);
            marker.Position=Sandbox.Game.EntityComponents.MyTargetingHelper.Instance.GetLockingPosition(target.Entity);
            marker.Distance=Vector3D.Distance(marker.Position,MyHudMarkerRender.GetDistanceMeasuringMatrix().Translation);
            marker.Color=MyHudMarkerRender.MyTargetIndicatorRender.GetTargetingColor(circle.TargetType,true).ToVector4();
            string relation=circle.TargetType.ToString();
            marker.Relation=relation=="Enemy" ? "Enemies":relation=="Friendly" ? "FactionShare":"Neutral";
        }

        private static void ReadPlayers(MyHudMarkerRender renderer,List<Marker> markers,Vector3D head)
        {
            var indicators=playerIndicators.GetValue(renderer) as IEnumerable;
            if(indicators==null) return;
            foreach(object entry in indicators)
            {
                object indicator=entry.GetType().GetProperty("Value").GetValue(entry);
                var character=indicatorEntity.GetValue(indicator) as MyCharacter;
                if(character==null || character.IsDead || character.Closed || character.MarkedForClose || character.PositionComp==null || character.RadioBroadcaster?.Enabled==true) continue;
                var tint=(VRageMath.Color)indicatorColor.GetValue(indicator);
                if(tint.A==0) continue;
                string relation=indicatorRelation.GetValue(indicator).ToString();
                relation=relation=="Self" ? "Owner":relation=="Allies" ? "FactionShare":relation=="Neutral" ? "Neutral":"Enemies";
                var position=character.PositionComp.GetPosition()+character.WorldMatrix.Up*(character.PositionComp.LocalAABB.Height+.02);
                string icon=relation=="Owner" ? "self":relation=="FactionShare" ? "friendly":relation=="Enemies" ? "enemy":"neutral";
                markers.Add(new Marker { Id="player:"+character.EntityId,Position=position,Entity=character.EntityId,Lift=character.PositionComp.LocalAABB.Height+.02f,Name=character.CustomNameWithFaction.ToString(),
                    Kind="Character",Relation=relation,Color=tint.ToVector4(),FontColor=tint.ToVector4(),Font="Blue",Pinned=(bool)indicatorPinned.GetValue(indicator),
                    Icon=@"Textures\HUD\marker_"+icon+".dds",Distance=Vector3D.Distance(position,head) });
            }
        }

        internal static bool Project(Vector3D world, MatrixD view, MatrixD projection, out Vector2 screen)
        {
            screen=Vector2.Zero;
            var local=Vector3D.Transform(world,view);
            if (!local.IsValid() || local.Z>=-0.05) return false;
            var clip=Vector4D.Transform(new Vector4D(local,1),projection);
            if (!(clip.X+clip.Y+clip.W).IsValid() || clip.W<=0) return false;
            screen=new Vector2((float)(clip.X/clip.W*0.5+0.5),(float)(0.5-clip.Y/clip.W*0.5));
            return screen.X>=0.025f && screen.X<=0.975f && screen.Y>=0.025f && screen.Y<=0.975f;
        }

        internal static string Distance(double metres)
        {
            var text=new System.Text.StringBuilder();
            MyHudMarkerRender.AppendDistance(text,metres);
            return text.ToString();
        }
        public static void BeginFrame(MatrixD head,View markers,Vector2? limits=null,MatrixD? trackingToWorld=null,ShipCrosshair.View crosshair=null,int eyeWidth=0)
        {
            renderSnapshot=markers?.Generation==generation ? markers : null;
            renderHead=head;
            renderCrosshair=crosshair;
            var options=SignalLayout.Options.Current;
            if(limits.HasValue) { options.LimitX=Math.Min(options.LimitX,limits.Value.X); options.LimitY=Math.Min(options.LimitY,limits.Value.Y); }
            if(eyeWidth>0) options.PixelTan=2*(float)StereoRenderState.PixelSlopeX/eyeWidth;
            renderOptions=options; markersVisible=HelmetHud.Markers;
            var windows=new List<SurfaceView>();
            foreach(var panel in RenderFrameBridge.Surfaces ?? new SurfaceView[0])
            {
                if(!panel.SignalWindow) continue;
                var s=SpatialUi.RenderSurface(panel,trackingToWorld);
                var local=Vector3D.Transform(s.Pose.Translation,MatrixD.Invert(head));
                if(local.Z<-.01 && Math.Abs(local.X/local.Z)<options.LimitX && Math.Abs(local.Y/local.Z)<options.LimitY &&
                    Vector3D.Dot(s.Pose.Backward,head.Translation-s.Pose.Translation)>0) windows.Add(s);
            }
            signalHead=head;
            signalUp=head.Up;
            if(Common.Config.CharacterMarkerRoll)
            {
                signalUp=(trackingToWorld ?? MatrixD.Invert(SpaceEngineersVR.Wrappers.MyRender11.Environment_Matrices.ViewD)).Up;
            }
            layout=SignalLayout.Build(renderSnapshot,head,options,DateTime.UtcNow);
            foreach(var window in windows) layout=WristSignals.OutsideWindow(layout,window,head);
        }

        // Within the signal limits, which both eyes see.
        private static bool InSharedView(Vector3D world)
        {
            var local=Vector3D.Transform(world,MatrixD.Invert(signalHead));
            return local.Z<-.05 && Math.Abs(local.X/local.Z)<renderOptions.LimitX && Math.Abs(local.Y/local.Z)<renderOptions.LimitY;
        }
        public static void Draw(Texture2D target, MatrixD view, MatrixD projection)
        {
            var current=renderSnapshot;
            if (failed || Main.MenuOpen || !Main.WorldAvailable) return;
            try
            {
                NativeSprites.Fade(target,HelmetHud.HudAlpha);
                if(ShipCrosshair.Enabled(Common.Config,HelmetHud.Visible)) ShipCrosshair.Draw(target,renderCrosshair,renderHead,view,projection,Common.Config.FaceMarkersTowardViewer);
                if(HelmetHud.Visible) { DamageMarkers.Draw(target,renderHead,view,projection); if(Common.Config.ShowHitMarkers) RemoteCombat.DrawImpact(target,renderHead,view,projection); }
                if(HelmetHud.Visible) BuildInfoLabels.Draw(target,signalHead,view,projection,Common.Config.FaceMarkersTowardViewer,signalUp);
                bool signals=current!=null && (DateTime.UtcNow-current.Time).TotalSeconds<=MaxCaptureAge && markersVisible;
                if(HelmetHud.Visible) VoiceMarkers.Draw(target,RenderFrameBridge.Talking,signals ? layout:null,signalHead,view,projection,Common.Config.FaceMarkersTowardViewer,signalUp);
                if(signals)
                {
                    SignalPainter.Draw(target,layout,signalHead,view,projection,Common.Config.FaceMarkersTowardViewer,signalUp);
                    var lead=current.Lead;
                    if(lead!=null && lead.Binocular && !(InSharedView(lead.Position) && InSharedView(lead.Target))) lead=null;
                    if(Common.Config.SignalRings) NativeLead.Draw(target,lead,view,projection,signalUp,Common.Config.FaceMarkersTowardViewer);
                }
            }
            catch (Exception ex) { recovery.Fail(ex,"VR marker drawing disabled"); }
            finally { NativeSprites.Fade(null,1); }
        }
        public static void Reset()
        {
            generation++; RenderFrameBridge.CaptureMarkers(null); renderSnapshot=null;
            renderCrosshair=null; DamageMarkers.Reset();
            layout=new SignalLayout.Entry[0]; WristSignals.Reset();
        }
    }
}
