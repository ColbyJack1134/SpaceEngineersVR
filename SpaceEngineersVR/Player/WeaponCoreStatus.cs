using System;
using System.Collections.Generic;
using System.Linq;
using SpaceEngineersVR.Plugin;
using VRage.Game.Entity;
using SpaceEngineersVR.Util;

namespace SpaceEngineersVR.Player
{
    // WeaponCore's top HUD (weapon status, ammo, reload/charge, heat) as a wrist page.
    // Identical weapons are stacked like WeaponCore does; the worst-off weapon of each stack is shown.
    internal static class WeaponCoreStatus
    {
        internal sealed class Row
        {
            public string Name,Status,Ammo;
            public int Count;
            public float? Progress;   // reload or charge 0..1, else heat 0..1
            public int Severity;      // 0 ok, 1 busy (reloading/charging/hot), 2 blocked (no ammo/overheated)
        }

        private const int MaxRows=12;
        private static Row[] rows=new Row[0];
        // Weapon list page on the wrist; rows are sorted worst first, so page 1 shows what needs attention.
        private static int page;
        private static DateTime next;

        internal static Row[] Rows
        {
            get
            {
                var now=DateTime.UtcNow;
                if(now<next) return rows;
                next=now.AddSeconds(.2);
                try { rows=Collect(); }
                catch(Exception ex) { rows=new Row[0]; Logger.Warning(ex,"WeaponCore wrist status read failed"); next=now.AddSeconds(5); }
                return rows;
            }
        }

        internal static void Reset() { rows=new Row[0]; next=DateTime.MinValue; page=0; }

        private static Row[] Collect()
        {
            if(!WeaponCoreInternals.Available || WeaponCoreInternals.TrackingAi==null) return new Row[0];
            uint tick=WeaponCoreInternals.Tick;
            var all=new List<Row>();
            foreach(var comp in WeaponCoreInternals.WeaponComps())
            {
                var entity=WeaponCoreInternals.CompEntity(comp);
                if(entity==null || entity.Closed || entity.MarkedForClose) continue;
                foreach(var weapon in WeaponCoreInternals.Weapons(comp)) all.Add(Read(weapon,entity,tick));
            }
            return all.GroupBy(r=>r.Name)
                .Select(g=> { var worst=g.OrderByDescending(r=>r.Severity).ThenBy(r=>r.Progress ?? 1).First(); worst.Count=g.Count(); return worst; })
                .OrderByDescending(r=>r.Severity).ThenBy(r=>r.Name).Take(MaxRows).ToArray();
        }

        private static Row Read(object weapon,MyEntity entity,uint tick)
        {
            string name=ModReflection.Value(weapon,"","FriendlyName");
            if(string.IsNullOrWhiteSpace(name)) name=(entity as Sandbox.ModAPI.IMyTerminalBlock)?.CustomName ?? entity.DisplayName ?? "Weapon";
            int ammo=ModReflection.Value(weapon,-1,"ProtoWeaponAmmo","CurrentAmmo");
            int magazine=ModReflection.Value(weapon,0,"ActiveAmmoDef","AmmoDef","Const","MagazineSize");
            bool mustCharge=ModReflection.Value(weapon,false,"ActiveAmmoDef","AmmoDef","Const","MustCharge");
            bool loading=ModReflection.Value(weapon,false,"Loading");
            bool outOfAmmo=ModReflection.Value(weapon,false,"OutOfAmmo");
            bool overheated=ModReflection.Value(weapon,false,"PartState","Overheated");
            float heat=ModReflection.Value(weapon,0f,"HeatPerc");
            var row=new Row {Name=name,Count=1,Ammo=ammo>=0 && magazine>0 ? ammo+" / "+magazine : ammo>=0 ? ammo.ToString() : ""};

            if(mustCharge && loading)
            {
                float charge=ModReflection.Value(weapon,0f,"ProtoWeaponAmmo","CurrentCharge");
                float max=ModReflection.Value(weapon,0f,"MaxCharge");
                row.Progress=max>0 ? Math.Max(0,Math.Min(1,charge/max)) : (float?)null;
                row.Status="Charging"; row.Severity=1;
            }
            else if(loading)
            {
                uint end=ModReflection.Value(weapon,uint.MaxValue,"ReloadEndTick");
                int time=ModReflection.Value(weapon,0,"System","WConst","ReloadTime");
                if(end<uint.MaxValue-1 && time>0) row.Progress=Math.Max(0,Math.Min(1,1-(float)((long)end-tick)/time));
                row.Status="Reloading"; row.Severity=1;
            }
            else if(overheated) { row.Status="Overheated"; row.Severity=2; row.Progress=Math.Min(1,heat); }
            else if(outOfAmmo) { row.Status="No ammo"; row.Severity=2; }
            else
            {
                row.Status=heat>=.01f ? "Heat "+Math.Round(heat*100)+"%" : "Ready";
                if(heat>=.01f) { row.Progress=Math.Min(1,heat); row.Severity=heat>=.8f ? 1:0; }
            }
            return row;
        }

        // WeaponCore's 0-9 threat level from its offense rating (same thresholds as its target panel, without the shield-mod adjustment).
        // With Defense Shields loaded, WeaponCore shifts the level by one for whichever side has the stronger shield.
        internal static int ThreatLevel(float rating,int shieldBonus=0) =>
            rating>5 ? (shieldBonus<0 ? 8:9) : rating>4 ? 8+shieldBonus : rating>3 ? 7+shieldBonus : rating>2 ? 6+shieldBonus : rating>1 ? 5+shieldBonus :
            rating>.5f ? 4+shieldBonus : rating>.25f ? 3+shieldBonus : rating>.125f ? 2+shieldBonus : rating>.0625f ? 1+shieldBonus : rating>0 ? (shieldBonus>0 ? 1:0) : 0;

        internal static int ShieldBonus(MyEntity grid,MyEntity target)
        {
            if(!ShieldsBridge.Ready || grid==null || target==null) return 0;
            var mine=ShieldsBridge.Of(grid); var theirs=ShieldsBridge.Of(target);
            bool myUp=mine.Exists && mine.Online,theirUp=theirs.Exists && theirs.Online;
            return theirUp && myUp ? (theirs.Percent>mine.Percent ? 1:-1) : theirUp ? 1 : myUp ? -1 : 0;
        }

        // Target shield summary for labels, e.g. "S 74%" / "S down"; null when the target has no Defense Shield.
        internal static string ShieldLabel(MyEntity target)
        {
            if(!ShieldsBridge.Ready || target==null) return null;
            var info=ShieldsBridge.Of(target);
            return !info.Exists ? null : info.Online ? "S "+Math.Round(info.Percent)+"%" : "S down";
        }

        // Threat level of an entity among the grid's tracked threats; null when WeaponCore does not rate it as a threat.
        internal static int? Threat(MyEntity grid,MyEntity target)
        {
            if(grid==null || target==null) return null;
            var top=target.GetTopMostParent();
            foreach(var threat in WeaponCoreBridge.Threats(grid))
                if(threat.Item1!=null && threat.Item1.GetTopMostParent()==top) return ThreatLevel(threat.Item2,ShieldBonus(grid,top as MyEntity));
            return null;
        }

        // Locked target summary: name, threat, distance, relative speed, closing speed and size.
        internal static string TargetText(MyEntity grid,MyEntity focus,out string detail,out string size)
        {
            detail=size=null;
            if(grid==null || focus==null || focus.Closed) return null;
            var self=grid.PositionComp.WorldAABB.Center; var target=focus.PositionComp.WorldAABB.Center;
            var offset=target-self; double distance=offset.Length();
            var relative=(focus.Physics?.LinearVelocity ?? VRageMath.Vector3.Zero)-(grid.Physics?.LinearVelocity ?? VRageMath.Vector3.Zero);
            double closing=distance>1 ? -VRageMath.Vector3D.Dot(relative,offset/distance) : 0;
            size=focus is Sandbox.Game.Entities.MyCubeGrid cubes ? cubes.BlocksCount+" blocks · "+(cubes.GridSizeEnum==VRage.Game.MyCubeSize.Large ? "large":"small") : null;
            int? threat=Threat(grid,focus);
            string shield=ShieldLabel(focus);
            detail="Threat "+(threat.HasValue ? threat.Value+"/9" : "–")+(shield!=null ? "  ·  "+(shield.StartsWith("S down") ? "shield down":"shield "+shield.Substring(2)) : "")+"  ·  "+WorldMarkers.Distance(distance)+"  ·  "+Math.Round(relative.Length())+" m/s  ·  closing "+Math.Round(closing)+" m/s";
            return string.IsNullOrWhiteSpace(focus.DisplayName) ? "Target" : focus.DisplayName;
        }

        internal static void Keys(List<SurfaceKey> keys)
        {
            var grid=TargetLock.ShooterGrid();
            float y=.145f;
            keys.Add(new SurfaceKey("WEAPONS",.025f,y+.005f,.45f,.06f) {Caption=true,Enabled=false});
            keys.Add(new SurfaceKey("Manual aim",.515f,y,.46f,.07f) {Action=new ActionChoice("Manual aim",WeaponCoreAim.CycleMode),Value=WeaponCoreAim.ModeName});
            y+=.085f;
            string alert=WeaponCoreAlerts.Text;
            if(alert!=null) { keys.Add(new SurfaceKey("⚠  "+alert,.025f,y,.95f,.07f) {Enabled=false}); y+=.08f; }

            var focus=grid==null ? null:WeaponCoreBridge.Focus(grid);
            string name=TargetText(grid,focus,out string detail,out string size);
            if(name!=null)
            {
                // The detail line is too long to share the name's key.
                keys.Add(new SurfaceKey("Target: "+name,.025f,y,.95f,.07f) {Enabled=false,Value=size});
                keys.Add(new SurfaceKey(detail,.035f,y+.075f,.93f,.05f) {Caption=true,Enabled=false});
                y+=.135f;
            }
            bool? locked=WeaponCoreInternals.FocusLocked;
            keys.Add(new SurfaceKey("Focus lock",.025f,y,.30f,.07f) {Action=TargetLock.FocusLockAction,Enabled=grid!=null && locked.HasValue,Value=locked==true ? "On":"Off"});
            keys.Add(new SurfaceKey("Prev",.34f,y,.15f,.07f) {Action=TargetLock.PreviousTargetAction,Enabled=grid!=null});
            keys.Add(new SurfaceKey("Next",.50f,y,.15f,.07f) {Action=TargetLock.NextTargetAction,Enabled=grid!=null});
            keys.Add(new SurfaceKey("Release",.66f,y,.315f,.07f) {Action=TargetLock.ReleaseTargetAction,Enabled=focus!=null});
            y+=.085f;

            var current=Rows;
            int room=Math.Max(1,(int)((.895f-y)/.082f));
            if(current.Length==0)
                keys.Add(new SurfaceKey(WeaponCoreInternals.TrackingAi==null ? "Not controlling a WeaponCore grid" : "No weapons",.025f,y,.95f,.075f) {Enabled=false});
            // More weapon types than fit: page the list with a column of ▲ / page / ▼ keys to its right.
            int pages=Math.Max(1,(current.Length+room-1)/room);
            page=Math.Max(0,Math.Min(page,pages-1));
            float width=pages>1 ? .86f:.95f;
            for(int i=0;i<room && page*room+i<current.Length;i++)
            {
                var row=current[page*room+i];
                string label=row.Count>1 ? row.Name+"  ×"+row.Count : row.Name;
                string value=string.IsNullOrEmpty(row.Ammo) ? row.Status : row.Status+"   "+row.Ammo;
                var key=new SurfaceKey(label,.025f,y+i*.082f,width,.076f) {Enabled=false,Value=value};
                if(row.Progress.HasValue) key.Slider=row.Progress.Value;
                keys.Add(key);
            }
            if(pages>1)
            {
                float x=.895f,arrow=room>1 ? .076f:.036f,last=room>1 ? y+(room-1)*.082f:y+.04f;
                keys.Add(new SurfaceKey("▲",x,y,.08f,arrow) {Enabled=page>0,Action=new ActionChoice("Previous weapons",()=>page--)});
                keys.Add(new SurfaceKey("▼",x,last,.08f,arrow) {Enabled=page+1<pages,Action=new ActionChoice("Next weapons",()=>page++)});
                if(room>2) keys.Add(new SurfaceKey((page+1)+" / "+pages,x,y+.082f,.08f,(room-2)*.082f-.006f) {Enabled=false});
            }

            keys.Add(new SurfaceKey("Back",.025f,.91f,.15f,.07f) {Action=new ActionChoice("Back",()=>WristPanel.Show(0))});
            keys.Add(new SurfaceKey("Lock ahead",.19f,.91f,.25f,.07f) {Action=TargetLock.LockAheadAction,Enabled=grid!=null});
            keys.Add(new SurfaceKey(WeaponCoreAim.PaintArmed ? "Cancel paint":"Paint",.455f,.91f,.25f,.07f) {Action=TargetLock.PaintAction,Enabled=grid!=null && !WeaponCoreInternals.PaintProhibited,Active=WeaponCoreAim.PaintArmed || WeaponCoreAim.Painted});
            keys.Add(new SurfaceKey("Clear paint",.72f,.91f,.255f,.07f) {Action=TargetLock.ClearPaintAction,Enabled=grid!=null && WeaponCoreAim.Painted});
        }
    }
}
