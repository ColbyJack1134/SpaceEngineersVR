using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Game.Entities;
using Sandbox.Game.World;
using SpaceEngineersVR.Plugin;
using VRage.Game.Entity;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    // WeaponCore targeting drawn by the VR marker renderer, so it is correct in both eyes and visible alongside a camera feed:
    // the focused (locked) target, tracked threats, the lead pip for the focus, and the manual aim point.
    // Uses WeaponCore's public API except for picking a reference weapon for the lead (WeaponCoreInternals).
    internal static class WeaponCoreMarkers
    {
        private const int MaxThreats=8;
        private static readonly Vector4 focusColor=new Vector4(1,.25f,.2f,1),threatColor=new Vector4(1,.55f,.2f,.85f),aimColor=new Vector4(.85f,.95f,1,.9f);
        private static MyEntity leadWeapon;
        private static DateTime leadWeaponTime;

        internal static void Add(List<WorldMarkers.Marker> markers)
        {
            if(!WeaponCoreBridge.ApiReady) return;
            try
            {
                var grid=Shooter();
                if(grid==null) return;
                var head=CameraRig.Current?.Anchor.Translation ?? MySector.MainCamera.Position;
                var own=grid.GetTopMostParent();
                var focus=WeaponCoreBridge.Focus(grid);
                if(focus!=null && !focus.Closed && !focus.MarkedForClose)
                {
                    var position=focus.PositionComp.WorldAABB.Center;
                    markers.Add(new WorldMarkers.Marker {Kind="Target",Id="wcfocus:"+focus.EntityId,Name="LOCKED  "+Name(focus)+(WeaponCoreStatus.Threat(grid,focus) is int level ? "  T"+level:"")+Shield(focus),Relation="Enemies",
                        Color=focusColor,FontColor=focusColor,Pinned=true,LockState="Locked",LockProgress=1,Position=position,Distance=Vector3D.Distance(position,head)});
                }
                int shown=0;
                foreach(var threat in WeaponCoreBridge.Threats(grid))
                {
                    var target=threat.Item1;
                    if(target==null || target.Closed || target.MarkedForClose || target==focus || target.GetTopMostParent()==own) continue;
                    if(++shown>MaxThreats) break;
                    var position=target.PositionComp.WorldAABB.Center;
                    markers.Add(new WorldMarkers.Marker {Kind="Target",Id="wcthreat:"+target.EntityId,Name=Name(target)+"  T"+WeaponCoreStatus.ThreatLevel(threat.Item2,WeaponCoreStatus.ShieldBonus(grid,target))+Shield(target),Relation="Enemies",
                        Color=threatColor,FontColor=threatColor,Position=position,Distance=Vector3D.Distance(position,head)});
                }
                if(WeaponCoreAim.AimPoint.HasValue && (DateTime.UtcNow-WeaponCoreAim.AimTime).TotalSeconds<.25 && WeaponCoreAim.ManualActive)
                {
                    var position=WeaponCoreAim.AimPoint.Value;
                    markers.Add(new WorldMarkers.Marker {Kind="Target",Id="wcaim",Name="AIM",Relation="Neutral",Color=aimColor,FontColor=aimColor,
                        Pinned=true,Position=position,Distance=Vector3D.Distance(position,head)});
                }
            }
            catch(Exception ex) { Logger.Warning(ex,"WeaponCore VR markers failed this frame"); }
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
                if(!WeaponCoreInternals.Value(comp,false,"HasTurret")) { leadWeapon=entity; return entity; }
                if(turret==null) turret=entity;
            }
            return leadWeapon=turret;
        }

        private static MyEntity Shooter()
        {
            var controlled=MySession.Static?.ControlledEntity?.Entity as MyCubeBlock;
            var grid=controlled?.CubeGrid;
            return WeaponCoreBridge.HasAi(grid) ? grid:null;
        }

        private static string Shield(MyEntity entity) { string label=WeaponCoreStatus.ShieldLabel(entity); return label==null ? "" : "  "+label; }

        private static string Name(MyEntity entity) => string.IsNullOrWhiteSpace(entity.DisplayName) ? "Target" : entity.DisplayName;

        internal static void Reset() { leadWeapon=null; }
    }
}
