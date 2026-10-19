using System;
using VRage.Game.Entity;

namespace SpaceEngineersVR.Player
{
    internal static class WeaponCoreThreats
    {
        // WeaponCore's offense-rating thresholds for threat levels 1-9, as its target panel uses them.
        private static readonly float[] ratingSteps={.0625f,.125f,.25f,.5f,1,2,3,4,5};

        // WeaponCore's 0-9 threat level. With Defense Shields loaded it shifts the level by one toward the side whose shield is stronger.
        internal static int ThreatLevel(float rating,int shieldBonus=0)
        {
            if(rating<=0) return 0;
            int level=0;
            while(level<ratingSteps.Length && rating>ratingSteps[level]) level++;
            return Math.Max(0,Math.Min(9,level+shieldBonus));
        }

        internal static int ShieldBonus(MyEntity grid,MyEntity target)
        {
            if(!ShieldsBridge.Ready || grid==null || target==null) return 0;
            var mine=ShieldsBridge.Of(grid); var theirs=ShieldsBridge.Of(target);
            bool myUp=mine.Exists && mine.Online,theirUp=theirs.Exists && theirs.Online;
            return theirUp && myUp ? (theirs.Percent>mine.Percent ? 1:-1) : theirUp ? 1 : myUp ? -1 : 0;
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

        // Marker shield summary, "S 74%" or "S down"; null when the target has no Defense Shield.
        internal static string ShieldLabel(MyEntity target)
        {
            if(!ShieldsBridge.Ready || target==null) return null;
            var info=ShieldsBridge.Of(target);
            return !info.Exists ? null : info.Online ? "S "+Math.Round(info.Percent)+"%" : "S down";
        }

        internal static string Name(MyEntity entity) => string.IsNullOrWhiteSpace(entity?.DisplayName) ? "Target" : entity.DisplayName;
    }
}
