using System;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal sealed class WeaponProfile
    {
        public readonly string Item, Model;
        public readonly Vector3 Primary, Support, Muzzle;
        private WeaponProfile(string item, string model, Vector3 primary, Vector3 support, Vector3 muzzle)
        { Item=item; Model=model; Primary=primary; Support=support; Muzzle=muzzle; }
        // Metres from the installed meshes; these are palm contacts, not wrist bones.
        public static readonly WeaponProfile Rifle = new WeaponProfile("AutomaticRifleItem", "Models/Weapons/AutomaticRifle.mwm",
            new Vector3(0,-0.043f,0.083f), new Vector3(0,0,-0.205f), new Vector3(0,0.048128f,-0.318859f));
        public static readonly WeaponProfile Launcher = new WeaponProfile("BasicHandHeldLauncherItem", "Models/Weapons/RocketLauncher_Regular.mwm",
            new Vector3(0,-0.015f,0.205f), new Vector3(0,0.05f,-0.235f), new Vector3(0,0.16771293f,-0.41278997f));
        public static WeaponProfile Find(string item, string model)
        {
            foreach (var profile in new[] { Rifle, Launcher })
                if (item == profile.Item && model != null && model.Replace('\\','/').EndsWith(profile.Model,StringComparison.OrdinalIgnoreCase)) return profile;
            return null;
        }
    }

    internal static class WeaponPose
    {
        // The wrist remains at (0,.02,.04); the grasp center lies 4 cm ahead of it.
        public static Vector3 Palm(Matrix grip) => Vector3.Transform(new Vector3(0,0.02f,0),grip);
        public static Matrix Anchor(WeaponProfile profile, Matrix orientation, Vector3 primary)
        {
            Matrix result=orientation.GetOrientation();
            result.Translation=primary-Vector3.TransformNormal(profile.Primary,result);
            return result;
        }
        public static bool TrySupport(WeaponProfile profile, Matrix oneHand, Vector3 primary, Vector3 support, out Matrix result)
        {
            result=oneHand;
            if (!oneHand.IsValid() || !primary.IsValid() || Math.Abs(oneHand.Determinant()-1)>0.01f) return false;
            Vector3 delta=support-primary;
            float distance=delta.Length();
            float span=Vector3.Distance(profile.Primary,profile.Support);
            if (!delta.IsValid() || distance<0.12f || distance>0.85f || Math.Abs(distance-span)>0.24f) return false;
            Vector3 from=Vector3.Normalize(Vector3.TransformNormal(profile.Support-profile.Primary,oneHand));
            Vector3 to=delta/distance;
            // Reject folding the support arm back through the firing hand.
            if (Vector3.Dot(from,to)<-0.25f) return false;
            result=Anchor(profile,ArmMath.AimBone(oneHand,from,to),primary);
            return result.IsValid();
        }
    }
}
