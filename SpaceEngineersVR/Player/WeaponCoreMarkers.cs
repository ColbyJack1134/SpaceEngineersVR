using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Game.Entities;
using Sandbox.Game.World;
using SpaceEngineersVR.Plugin;
using VRage.Game.Entity;
using VRageMath;
using SpaceEngineersVR.Util;

namespace SpaceEngineersVR.Player
{
    // WeaponCore targeting drawn by the VR marker renderer, correct in both eyes: the lock, the nearest threats, the lead pip,
    // the manual aim point and the paint mark and previews.
    internal static class WeaponCoreMarkers
    {
        private const int MaxThreats=8;
        private static readonly Vector4 focusColor=new Vector4(1,.25f,.2f,1),threatColor=new Vector4(1,.55f,.2f,.85f),aimColor=new Vector4(.85f,.95f,1,.9f),paintColor=new Vector4(1,.35f,.9f,1);
        private static MyEntity leadWeapon;
        private static DateTime leadWeaponTime;

        // Names, threat levels, shield labels and the ranking cost WeaponCore / Defense Shields calls and allocations; they
        // refresh at 10 Hz while positions and bracket sizes follow every frame.
        private sealed class Snapshot
        {
            public MyEntity Grid,Focus,PaintedEntity;
            public string FocusName;
            public readonly List<KeyValuePair<MyEntity,string>> Threats=new List<KeyValuePair<MyEntity,string>>();
            public Vector3D PaintedLocal;
            public bool Manual;
        }
        private static Snapshot snapshot;
        private static DateTime refresh;

        // Lock, cycle, release and paint changes show on the next frame instead of the next refresh.
        internal static void Invalidate() { refresh=DateTime.MinValue; }

        private static Snapshot Current(MyEntity grid)
        {
            var now=DateTime.UtcNow;
            if(snapshot!=null && snapshot.Grid==grid && now<refresh) return snapshot;
            refresh=now.AddSeconds(.1);
            var next=new Snapshot {Grid=grid,Focus=WeaponCoreBridge.Focus(grid),Manual=WeaponCoreAim.ManualActive};
            if(next.Focus!=null)
                next.FocusName="LOCKED  "+Name(next.Focus)+(WeaponCoreStatus.Threat(grid,next.Focus) is int level ? "  T"+level:"")+Shield(next.Focus);
            foreach(var threat in TargetLock.RankedThreats(grid).Where(t=>t.Item1!=next.Focus).Take(MaxThreats))
                next.Threats.Add(new KeyValuePair<MyEntity,string>(threat.Item1,
                    Name(threat.Item1)+"  T"+WeaponCoreStatus.ThreatLevel(threat.Item2,WeaponCoreStatus.ShieldBonus(grid,threat.Item1))+Shield(threat.Item1)));
            if(WeaponCoreAim.Painted) WeaponCoreInternals.PaintMark(out next.PaintedEntity,out next.PaintedLocal);
            return snapshot=next;
        }

        private static bool Alive(MyEntity entity) => entity!=null && !entity.Closed && !entity.MarkedForClose;

        internal static void Add(List<WorldMarkers.Marker> markers)
        {
            if(!WeaponCoreBridge.ApiReady) return;
            try
            {
                var grid=Shooter();
                if(grid==null) return;
                var head=CameraRig.Current?.Anchor.Translation ?? MySector.MainCamera.Position;
                var data=Current(grid);
                if(Alive(data.Focus))
                {
                    var locked=Marker("wcfocus:"+data.Focus.EntityId,data.FocusName,focusColor,data.Focus.PositionComp.WorldAABB.Center,head,Span(data.Focus,head,2.2f),pinned:true);
                    // Lays out first, so the other names move around it, and stays on the HUD as an arrow when out of view.
                    locked.LockState="Locked"; locked.EdgeArrow=true;
                    markers.Add(locked);
                }
                foreach(var threat in data.Threats)
                {
                    var target=threat.Key;
                    if(!Alive(target)) continue;
                    markers.Add(Marker("wcthreat:"+target.EntityId,threat.Value,threatColor,target.PositionComp.WorldAABB.Center,head,Span(target,head,1.7f)));
                }
                if(WeaponCoreAim.AimPoint.HasValue && (DateTime.UtcNow-WeaponCoreAim.AimTime).TotalSeconds<.25 && data.Manual)
                    markers.Add(Marker("wcaim","AIM",aimColor,WeaponCoreAim.AimPoint.Value,head,1,pinned:true,symbol:"cross",side:"below"));
                // WeaponCore's own paint mark is part of its hidden HUD; it rides on the painted entity.
                if(Alive(data.PaintedEntity))
                    markers.Add(Marker("wcpainted","PAINTED",paintColor,Vector3D.Transform(data.PaintedLocal,data.PaintedEntity.PositionComp.WorldMatrixRef),head,.9f,pinned:true,side:"upleft"));
                var lockPreview=HandLock.Preview;
                if(Alive(lockPreview))
                    markers.Add(Marker("wclock","LOCK  pull trigger",paintColor,lockPreview.PositionComp.WorldAABB.Center,head,1.2f,pinned:true,symbol:"cross",side:"upright"));
                if(WeaponCoreAim.PaintArmed && WeaponCoreAim.PaintPreview.HasValue)
                    markers.Add(Marker("wcpaint","PAINT  pull trigger",paintColor,WeaponCoreAim.PaintPreview.Value,head,1.2f,pinned:true,symbol:"cross",side:"upright"));
            }
            catch(Exception ex) { Logger.Warning(ex,"WeaponCore VR markers failed this frame"); }
        }

        private static WorldMarkers.Marker Marker(string id,string name,Vector4 color,Vector3D position,Vector3D head,float span,bool pinned=false,string symbol="brackets",string side=null) =>
            new WorldMarkers.Marker {Kind="WeaponCore",Id=id,Name=name,Relation="Enemies",Color=color,FontColor=color,Pinned=pinned,Position=position,
                Distance=Vector3D.Distance(position,head),Symbol=symbol,Span=span,LabelSide=side,AvoidOverlap=true};

        // Brackets frame the target's apparent size, never smaller than minimum (in standard marker icons).
        private static float Span(MyEntity target,Vector3D head,float minimum)
        {
            double distance=Math.Max(1,Vector3D.Distance(target.PositionComp.WorldAABB.Center,head));
            double diameter=2*Math.Atan(target.PositionComp.WorldVolume.Radius/distance);
            double icon=MarkerBillboard.IconAngle*.55*Math.Max(.1f,Common.Config.SignalIconScale);
            return (float)MathHelper.Clamp(diameter/icon*1.1,minimum,12);
        }

        // Lead pip for the focused target, predicted for the first fixed (non-turret) WeaponCore weapon; null when none applies.
        internal static NativeLead.View Lead()
        {
            if(!WeaponCoreBridge.ApiReady) return null;
            try
            {
                var grid=Shooter();
                var focus=grid==null ? null:WeaponCoreBridge.Focus(grid);
                if(focus==null || focus.Closed || focus.MarkedForClose) return null;
                var weapon=LeadWeapon();
                if(weapon==null) return null;
                var predicted=WeaponCoreBridge.PredictedPosition(weapon,focus);
                var center=focus.PositionComp.WorldAABB.Center;
                bool inRange=predicted.HasValue;
                return new NativeLead.View {Position=predicted ?? center,Target=center,InRange=inRange,CircleSize=new Vector2(64,64),
                    RangeTextSize=inRange ? Vector2.Zero:NativeLead.MeasureRangeText(),Color=inRange ? focusColor:new Vector4(.7f,.7f,.7f,.8f)};
            }
            catch(Exception ex) { Logger.Warning(ex,"WeaponCore VR lead failed this frame"); return null; }
        }

        private static MyEntity LeadWeapon()
        {
            var now=DateTime.UtcNow;
            if(leadWeapon!=null && !leadWeapon.Closed && (now-leadWeaponTime).TotalSeconds<2) return leadWeapon;
            leadWeapon=null; leadWeaponTime=now;
            if(!WeaponCoreInternals.Available) return null;
            MyEntity turret=null;
            foreach(var comp in WeaponCoreInternals.WeaponComps())
            {
                var entity=WeaponCoreInternals.CompEntity(comp);
                if(entity==null || entity.Closed || entity.MarkedForClose) continue;
                if(!ModReflection.Value(comp,false,"HasTurret")) { leadWeapon=entity; return entity; }
                if(turret==null) turret=entity;
            }
            return leadWeapon=turret;
        }

        // Same rule as the lock actions: a WeaponCore seat or turret, or on foot a guided hand weapon.
        private static MyEntity Shooter() => TargetLock.ShooterGrid() ?? HandLock.Shooter();

        private static string Shield(MyEntity entity) { string label=WeaponCoreStatus.ShieldLabel(entity); return label==null ? "" : "  "+label; }

        private static string Name(MyEntity entity) => string.IsNullOrWhiteSpace(entity.DisplayName) ? "Target" : entity.DisplayName;

        internal static void Reset() { leadWeapon=null; snapshot=null; }
    }
}
