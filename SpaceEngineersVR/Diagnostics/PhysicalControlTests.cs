using System;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Player.Control;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class PhysicalControlTests
    {
        private static void Require(bool value,string name) { if (!value) throw new Exception(name); }
        private static void Near(Vector3 actual,Vector3 expected,string name) => Require(Vector3.Distance(actual,expected)<0.0001f,name);
        public static void Run(Action<string> log)
        {
            var grab=new GripCapture();
            Require(!grab.Update(true,true,true,true),"Held input captured at startup");
            grab.Update(true,false,true,true);
            Require(grab.Update(true,true,true,true) && grab.Held && grab.Consumed,"Nearby fresh squeeze not captured");
            Require(grab.AnalogDown(0,0.4f) && !grab.AnalogDown(0,0.08f),"Partial grip release leaks analog descent");
            grab.Update(false,true,false,false);
            Require(!grab.Held && grab.Consumed,"Forced release leaked grip to fallback");
            Require(!grab.Update(true,true,true,true),"Held input recaptured after lost tracking");
            grab.Update(true,false,true,true);
            Require(!grab.Consumed && grab.Update(true,true,true,true),"Release/regrab failed");
            grab.Release();
            Require(!grab.Update(true,true,true,true),"Mode transition recaptured held input");
            grab.Update(true,false,false,true);
            Require(!grab.Update(true,true,false,true),"Distant squeeze captured");
            Require(!grab.Update(true,true,true,true),"Dragging a held squeeze onto contact captured");
            foreach (var profile in new[] { WeaponProfile.Rifle, WeaponProfile.Launcher })
            {
                Require(WeaponProfile.Find(profile.Item,profile.Model)==profile,"Weapon profile matching");
                Require(WeaponProfile.Find(profile.Item,"modded.mwm")==null,"Profile applied to modded geometry");
                for (int i=0;i<240;i++)
                {
                    Matrix basis=Matrix.CreateFromYawPitchRoll(i*0.13f,i*0.037f,i*0.02f);
                    basis.Translation=new Vector3(0.25f,1.4f,-0.3f);
                    Vector3 primary=WeaponPose.Palm(basis);
                    Matrix one=WeaponPose.Anchor(profile,basis,primary);
                    Near(Vector3.Transform(profile.Primary,one),primary,"Primary contact moved");
                    Vector3 support=Vector3.Transform(profile.Support,one)+basis.Up*0.06f;
                    Require(WeaponPose.TrySupport(profile,one,primary,support,out Matrix two),"Valid support rejected");
                    Near(Vector3.Transform(profile.Primary,two),primary,"Two-hand primary contact moved");
                    Near(Vector3.Normalize(Vector3.TransformNormal(profile.Support-profile.Primary,two)),
                        Vector3.Normalize(support-primary),"Support axis incorrect");
                    Matrix rigid=Matrix.CreateFromYawPitchRoll(0.31f,-0.18f,0.41f)*Matrix.CreateTranslation(18,-7,11);
                    Require(WeaponPose.TrySupport(profile,one*rigid,Vector3.Transform(primary,rigid),Vector3.Transform(support,rigid),out Matrix moved),"Rigid pose rejected");
                    Near((two*rigid).Forward,moved.Forward,"Aim changed with frame origin");
                    Near(Vector3.Transform(profile.Muzzle,two*rigid),Vector3.Transform(profile.Muzzle,moved),"Muzzle changed with frame origin");
                    Require(!WeaponPose.TrySupport(profile,one,primary,primary,out _),"Coincident hands accepted");
                    Require(!WeaponPose.TrySupport(profile,one,primary,primary+basis.Forward*2,out _),"Excess reach accepted");
                    Require(!WeaponPose.TrySupport(profile,one,primary,new Vector3(float.NaN),out _),"Invalid pose accepted");
                    Require(!WeaponPose.TrySupport(profile,new Matrix(),primary,support,out _),"Degenerate primary orientation accepted");
                }
            }
            log("PASS physical grabs: fresh squeeze, proximity, reach, tracking/mode release, no held-input ownership leak; 480 two-hand contact, muzzle, rigid-frame and degenerate-pose checks");
        }
    }
}
