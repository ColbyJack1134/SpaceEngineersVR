using System;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal enum ItemKind { Rifle, Pistol, Launcher, Welder, Grinder, Drill }
    internal sealed class WeaponProfile
    {
        internal readonly string Item,Model;
        internal readonly ItemKind Kind;
        internal readonly Vector3 Primary,Support,Muzzle,RightShift,LeftShift,Magazine;
        internal bool Tool => Kind>=ItemKind.Welder;
        internal bool Brace => Kind==ItemKind.Pistol || Kind==ItemKind.Welder;
        internal Vector3 Direction => Kind==ItemKind.Welder ? new Vector3(0,.46947156f,-.88294759f):Vector3.Forward;
        private WeaponProfile(string item,string model,ItemKind kind,Vector3 primary,Vector3 support,Vector3 muzzle,Vector3 right,Vector3 left,Vector3 magazine)
        { Item=item; Model="Models/Weapons/"+model+".mwm"; Kind=kind; Primary=primary; Support=support; Muzzle=muzzle; RightShift=right; LeftShift=left; Magazine=magazine; }
        // Native palm frames, with mesh-measured corrections; effect pivots are not tool contact points.
        internal static readonly WeaponProfile[] All={
            new WeaponProfile("AutomaticRifleItem", "AutomaticRifle", ItemKind.Rifle, new Vector3(0.02676702f,-0.03522223f,0.18000007f), new Vector3(-0.08030498f,-0.04085726f,-0.1366592f), new Vector3(0.00000013f,0.04812809f,-0.31885853f), new Vector3(0f,0f,0f), new Vector3(0f,0f,0f), new Vector3(0f,-0.10668955f,-0.07233407f)),
            new WeaponProfile("PreciseAutomaticRifleItem", "PreciseAutomaticRifle", ItemKind.Rifle, new Vector3(0.02676702f,-0.03522223f,0.18000007f), new Vector3(-0.08030498f,-0.04085726f,-0.1366592f), new Vector3(0.00000013f,0.04812809f,-0.31885853f), new Vector3(0f,0f,0f), new Vector3(0f,0f,0f), new Vector3(0f,-0.09042368f,-0.06554391f)),
            new WeaponProfile("RapidFireAutomaticRifleItem", "RapidFireAutomaticRifle", ItemKind.Rifle, new Vector3(0.02676702f,-0.03522223f,0.18000007f), new Vector3(-0.08030498f,-0.04085726f,-0.1366592f), new Vector3(0.00000013f,0.04812809f,-0.31885853f), new Vector3(0f,0f,0f), new Vector3(0f,0f,0f), new Vector3(0f,-0.19320688f,-0.07912423f)),
            new WeaponProfile("UltimateAutomaticRifleItem", "UltimateAutomaticRifle", ItemKind.Rifle, new Vector3(0.02676702f,-0.03522223f,0.18000007f), new Vector3(-0.08030498f,-0.04085726f,-0.1366592f), new Vector3(0.00000013f,0.04812809f,-0.31885853f), new Vector3(0f,0f,0f), new Vector3(0f,0f,0f), new Vector3(0f,-0.12747202f,-0.07080819f)),
            new WeaponProfile("SemiAutoPistolItem", "Pistol_Warfare", ItemKind.Pistol, new Vector3(0.01643658f,-0.01656988f,0.18354118f), new Vector3(-0.09566162f,-0.07444992f,0.13949154f), new Vector3(0f,0.067419f,-0.17239414f), new Vector3(0f,0f,0f), new Vector3(-0.008f,0.028f,0.008f), new Vector3(0f,-0.09267507f,0.06955369f)),
            new WeaponProfile("FullAutoPistolItem", "Pistol_FullAuto_Warfare", ItemKind.Pistol, new Vector3(0.01643658f,-0.01656988f,0.18354118f), new Vector3(-0.09566162f,-0.07444992f,0.13949154f), new Vector3(0f,0.06741901f,-0.17239273f), new Vector3(0f,0f,0f), new Vector3(-0.008f,0.028f,0.008f), new Vector3(0f,-0.16958985f,0.08965235f)),
            new WeaponProfile("ElitePistolItem", "Pistol_Elite_Warfare", ItemKind.Pistol, new Vector3(0.01643658f,-0.01656988f,0.18354118f), new Vector3(-0.09566162f,-0.07444992f,0.13949154f), new Vector3(0f,0.06741901f,-0.17239273f), new Vector3(0f,0f,0f), new Vector3(-0.008f,0.028f,0.008f), new Vector3(0f,-0.09287956f,0.0760093f)),
            new WeaponProfile("FlareGunItem", "Pistol_FlareGun", ItemKind.Pistol, new Vector3(0.01643658f,-0.01656988f,0.18354118f), new Vector3(-0.09566162f,-0.07444992f,0.13949154f), new Vector3(0f,0.06341899f,-0.10469195f), new Vector3(0f,0f,0f), new Vector3(-0.008f,0.028f,0.008f), new Vector3(0f,-0.09649658f,0.08415222f)),
            new WeaponProfile("BasicHandHeldLauncherItem", "RocketLauncher_Regular", ItemKind.Launcher, new Vector3(0.02676702f,-0.03522223f,0.18000007f), new Vector3(-0.08030498f,-0.04085726f,-0.1366592f), new Vector3(0.00000014f,0.16771293f,-0.41278997f), new Vector3(0f,0f,0f), new Vector3(0f,0f,0f), new Vector3(-0.00001526f,-0.03022767f,-0.20481059f)),
            new WeaponProfile("AdvancedHandHeldLauncherItem", "RocketLauncher_Precision", ItemKind.Launcher, new Vector3(0.02676702f,-0.03522223f,0.18000007f), new Vector3(-0.08030498f,-0.04085726f,-0.1366592f), new Vector3(-0.00000004f,0.16770662f,-0.41881937f), new Vector3(0f,0f,0f), new Vector3(0f,0f,0f), new Vector3(-0.00001526f,-0.0294178f,-0.20481059f)),
            new WeaponProfile("WelderItem", "Welder", ItemKind.Welder, new Vector3(0.02191496f,-0.0974584f,0.12053466f), new Vector3(-0.08084223f,-0.12751487f,0.04795811f), new Vector3(0f,0.190192f,-0.069031f), new Vector3(0f,-0.035f,0f), new Vector3(-0.00053778f,-0.01331996f,0.11589689f), new Vector3(0f,0f,0f)),
            new WeaponProfile("Welder2Item", "WelderMk2", ItemKind.Welder, new Vector3(0.02191496f,-0.0974584f,0.12053466f), new Vector3(-0.08084223f,-0.12751487f,0.04795811f), new Vector3(0f,0.190192f,-0.069031f), new Vector3(0f,-0.035f,0f), new Vector3(-0.00053778f,-0.01331996f,0.11589689f), new Vector3(0f,0f,0f)),
            new WeaponProfile("Welder3Item", "WelderMk3", ItemKind.Welder, new Vector3(0.02191496f,-0.0974584f,0.12053466f), new Vector3(-0.08084223f,-0.12751487f,0.04795811f), new Vector3(0f,0.190192f,-0.069031f), new Vector3(0f,-0.035f,0f), new Vector3(-0.00053778f,-0.01331996f,0.11589689f), new Vector3(0f,0f,0f)),
            new WeaponProfile("Welder4Item", "WelderMk4", ItemKind.Welder, new Vector3(0.02191496f,-0.09276381f,0.12053466f), new Vector3(-0.08084223f,-0.12282028f,0.04795811f), new Vector3(0f,0.190192f,-0.069031f), new Vector3(0f,-0.035f,0f), new Vector3(-0.00053778f,-0.00862538f,0.11589689f), new Vector3(0f,0f,0f)),
            new WeaponProfile("AngleGrinderItem", "AngleGrinder", ItemKind.Grinder, new Vector3(0.10005532f,0.17532301f,0.308f), new Vector3(-0.10284656f,0.15638113f,-0.08030498f), new Vector3(0.05974f,-0.024f,-0.25f), new Vector3(-0.015f,0f,0.14f), new Vector3(0f,0f,0f), new Vector3(0f,0f,0f)),
            new WeaponProfile("AngleGrinder2Item", "AngleGrinderMk2", ItemKind.Grinder, new Vector3(0.10005532f,0.17532301f,0.308f), new Vector3(-0.10284656f,0.15638113f,-0.08030498f), new Vector3(0.05974f,-0.024f,-0.25f), new Vector3(-0.015f,0f,0.14f), new Vector3(0f,0f,0f), new Vector3(0f,0f,0f)),
            new WeaponProfile("AngleGrinder3Item", "AngleGrinderMk3", ItemKind.Grinder, new Vector3(0.10005532f,0.17532301f,0.308f), new Vector3(-0.10284656f,0.15638113f,-0.08030498f), new Vector3(0.05974f,-0.024f,-0.25f), new Vector3(-0.015f,0f,0.14f), new Vector3(0f,0f,0f), new Vector3(0f,0f,0f)),
            new WeaponProfile("AngleGrinder4Item", "AngleGrinderMk4", ItemKind.Grinder, new Vector3(0.10005532f,0.17532301f,0.308f), new Vector3(-0.10284656f,0.15638113f,-0.08030498f), new Vector3(0.05974f,-0.024f,-0.25f), new Vector3(-0.015f,0f,0.14f), new Vector3(0f,0f,0f), new Vector3(0f,0f,0f)),
            new WeaponProfile("HandDrillItem", "HandDrill", ItemKind.Drill, new Vector3(0.0328728f,0.01404145f,0.35064566f), new Vector3(-0.2301122f,-0.00000072f,-0.07670468f), new Vector3(0f,0f,-0.73f), new Vector3(0f,0.025f,0f), new Vector3(0f,0f,0f), new Vector3(0f,0f,0f)),
            new WeaponProfile("HandDrill2Item", "HandDrillMk2", ItemKind.Drill, new Vector3(0.0328728f,0.01404145f,0.35064566f), new Vector3(-0.2301122f,-0.00000072f,-0.07670468f), new Vector3(0f,0f,-0.73f), new Vector3(0f,0.025f,0f), new Vector3(0f,0f,0f), new Vector3(0f,0f,0f)),
            new WeaponProfile("HandDrill3Item", "HandDrillMk3", ItemKind.Drill, new Vector3(0.0328728f,0.01404145f,0.35064566f), new Vector3(-0.2301122f,-0.00000072f,-0.07670468f), new Vector3(0f,0f,-0.73f), new Vector3(0f,0.025f,0f), new Vector3(0f,0f,0f), new Vector3(0f,0f,0f)),
            new WeaponProfile("HandDrill4Item", "HandDrillMk4", ItemKind.Drill, new Vector3(0.0328728f,0.01404145f,0.35064566f), new Vector3(-0.2301122f,-0.00000072f,-0.07670468f), new Vector3(0f,0f,-0.73f), new Vector3(0f,0.025f,0f), new Vector3(0f,0f,0f), new Vector3(0f,0f,0f)),
        };
        internal static WeaponProfile Rifle => All[0];
        internal static WeaponProfile Launcher => All[8];
        internal MatrixD Palm(MatrixD native,bool left)
        {
            native.Translation+=left ? LeftShift:RightShift;
            if(!left && Kind==ItemKind.Welder)
            {
                var position=native.Translation;
                native=native.GetOrientation()*MatrixD.CreateRotationX(MathHelper.ToRadians(8)); native.Translation=position;
            }
            if(left && Kind==ItemKind.Welder)
                native=MatrixD.CreateWorld(native.Translation,new Vector3D(-.54319996,.54061740,-.64236197),new Vector3D(-.05577585,-.78662933,-.61492549));
            return native;
        }
        internal static WeaponProfile Find(string item,string model)
        {
            if(model==null) return null;
            foreach(var p in All) if(item==p.Item && model.Replace('\\','/').EndsWith(p.Model,StringComparison.OrdinalIgnoreCase)) return p;
            return null;
        }
        internal Vector3 SupportContact(MatrixD nativeLeft) => (Vector3)Vector3D.Transform(WeaponPose.GrabOffset,Palm(nativeLeft,true));
        internal int Grab(Vector3 point,Vector3 supportPoint)
        {
            bool magazine=ReloadAt(point);
            float supportDistance=Vector3.Distance(point,supportPoint),magazineDistance=Vector3.Distance(point,Magazine);
            if(magazine && (Kind==ItemKind.Pistol || magazineDistance<supportDistance)) return 2;
            return supportDistance<.12f ? 1:magazine ? 2:0;
        }
        internal bool ReloadAt(Vector3 point)
        {
            if(Tool) return false;
            var d=point-Magazine;
            // Pistol support wraps the side; reload can only be acquired below its magazine end face.
            return Kind==ItemKind.Pistol ? Math.Abs(d.X)<.075f && Math.Abs(d.Z)<.08f && d.Y<.012f && d.Y>-.12f : d.LengthSquared()<.105f*.105f;
        }
        internal Quaternion FingerRotation(string name,float trigger,bool support,Quaternion? native=null)
        {
            if(Kind==ItemKind.Pistol && !support && native.HasValue)
            {
                if(!name.Contains("Index")) return native.Value;
                float squeeze=name.EndsWith("_1") ? .06f:name.EndsWith("_2") ? .12f:.08f;
                return Quaternion.CreateFromAxisAngle(Vector3.Backward,squeeze*MathHelper.Clamp(trigger,0,1))*native.Value;
            }
            float curl=name.Contains("Thumb") ? .35f:.8f;
            if(name.Contains("Index"))
            {
                int joint=name.EndsWith("_1") ? 0:name.EndsWith("_2") ? 1:2;
                float rest=joint==0 ? .35f:joint==1 ? .55f:.45f;
                float pressed=joint==0 ? .44f:joint==1 ? .78f:.61f;
                curl=support ? .8f:MathHelper.Lerp(rest,pressed,MathHelper.Clamp(trigger,0,1));
            }
            return Quaternion.CreateFromAxisAngle(Vector3.Backward,curl);
        }
    }

    internal static class WeaponPose
    {
        internal static readonly Vector3 GrabOffset=new Vector3(-.07155358f,-.02681937f,.00165622f);
        public static Vector3 Palm(Matrix grip) => Vector3.Transform(new Vector3(0,.02f,0),grip);
        public static Matrix Anchor(WeaponProfile profile,Matrix orientation,Vector3 primary)
        {
            var result=orientation.GetOrientation();
            result.Translation=primary-Vector3.TransformNormal(profile.Primary,result); return result;
        }
        public static bool TryHandItem(MatrixD attachment,MatrixD palm,out MatrixD model)
        {
            model=MatrixD.Identity;
            if(!attachment.IsValid() || Math.Abs(attachment.Determinant())<1e-6 || !palm.IsValid()) return false;
            model=MatrixD.Invert(attachment)*palm; return model.IsValid();
        }
        internal static MatrixD Current(WeaponProfile profile,MatrixD one,MatrixD parent,MatrixD? support,Quaternion release,float blend)
        {
            var local=(Matrix)(one*MatrixD.Invert(parent));
            var primary=Vector3.Transform(profile.Primary,local);
            if(support.HasValue)
            {
                var off=(Vector3)Vector3D.Transform(support.Value.Translation,MatrixD.Invert(parent));
                TrySupport(profile,local,primary,off,out var two,true);
                return two*parent;
            }
            if(blend<=0) return one;
            var correction=Matrix.CreateFromQuaternion(Quaternion.Slerp(Quaternion.Identity,release,blend));
            return Anchor(profile,correction*local.GetOrientation(),primary)*parent;
        }
        public static bool TrySupport(WeaponProfile profile,Matrix oneHand,Vector3 primary,Vector3 support,out Matrix result,bool held=false)
        {
            result=oneHand;
            if(!oneHand.IsValid() || !primary.IsValid() || Math.Abs(oneHand.Determinant()-1)>.01f) return false;
            var delta=support-primary; float distance=delta.Length(),span=Vector3.Distance(profile.Primary,profile.Support);
            if(!delta.IsValid()) return false;
            if(!held && (distance<.055f || distance>.85f || Math.Abs(distance-span)>.24f)) return false;
            if(profile.Brace || distance<.055f) return true;
            var from=Vector3.Normalize(Vector3.TransformNormal(profile.Support-profile.Primary,oneHand)); var to=delta/distance;
            if(!held && Vector3.Dot(from,to)<-.25f) return false;
            result=Anchor(profile,ArmMath.AimBone(oneHand,from,to),primary); return result.IsValid();
        }
    }
}
