using System;
using SpaceEngineersVR.Player;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class CockpitReferenceTests
    {
        internal static void Run(Action<string> log)
        {
            foreach(var rig in CockpitRig.All)
            foreach(var stick in new[] {rig.Left,rig.Right})
            {
                if(stick==null) continue;
                Matrix authored=stick.Palm(stick==rig.Left);
                foreach(float grabAngle in new[] {0f,15f,90f,-90f})
                {
                    Matrix tilt=Matrix.Transpose(stick.Frame)*Matrix.CreateRotationX(MathHelper.ToRadians(-grabAngle))*stick.Frame;
                    Matrix captured=authored*tilt;
                    foreach(float angle in new[] {0f,15f,30f,-30f})
                    {
                        Matrix motion=Matrix.Transpose(stick.Frame)*Matrix.CreateRotationX(MathHelper.ToRadians(-angle))*stick.Frame;
                        var input=CockpitStickMath.Rotation(captured,captured*motion,.08f,false,1,stick.Frame);
                        Vector3 move=Vector3.Zero; Vector2 rotate=Vector2.Zero; float roll=0;
                        CockpitStickMath.ApplyFlight(false,true,Vector3.Zero,input,0,0,1,1,ref move,ref rotate,ref roll);
                        var visual=CockpitStickMath.CommandVisual(input,2,false);
                        if(Math.Abs(rotate.X-visual.X)>.0001f || Math.Abs(rotate.Y-visual.Y)>.0001f)
                            throw new Exception("Grab-relative stick visual/input mismatch: "+rig.Subtype);
                        if(angle==0 && (rotate!=Vector2.Zero || roll!=0)) throw new Exception("Capturing a tilted wrist did not start neutral");
                        if(Math.Abs(angle)==30 && Math.Abs(rotate.X)<.999f) throw new Exception("Grab-relative motion lost full travel");
                    }
                }
            }
            Vector3 translated=new Vector3(.7f,.4f,-.5f);
            Vector3 movement=Vector3.Zero; Vector2 rotation=Vector2.Zero; float bank=0;
            CockpitStickMath.ApplyFlight(true,false,translated,Vector3.Zero,.2f,0,1,1,ref movement,ref rotation,ref bank,2,2,.1f);
            var axes=CockpitStickMath.CommandVisual(translated,2,true);
            if(Vector3.Distance(movement,new Vector3(axes.Z+.1f,axes.Y+.2f,-axes.X))>.0001f)
                throw new Exception("Translation response curved twice or affected thumb input");
            float zoom=0; Vector2 aim=Vector2.Zero;
            CockpitStickMath.ApplyTurret(true,true,new Vector3(.5f),translated,1,2,ref aim,ref zoom,.2f);
            if(Math.Abs(aim.X-.25f)>.0001f || Math.Abs(zoom+.3f)>.0001f)
                throw new Exception("Turret response changed zoom curve or thumb input");
            log("PASS grab-relative joystick neutral at arbitrary wrist tilt, full relative travel, command-matched visuals and independent thumb input");
        }
    }
}
