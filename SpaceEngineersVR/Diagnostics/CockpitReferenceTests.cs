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
                bool left=stick==rig.Left;
                Matrix authored=stick.Palm(left);
                Matrix Turn(Matrix axis) => Matrix.Transpose(stick.Frame)*axis*stick.Frame;
                foreach(float grabAngle in new[] {0f,15f,90f,-90f})
                {
                    Matrix captured=authored*Turn(Matrix.CreateRotationX(MathHelper.ToRadians(-grabAngle)));
                    foreach(float angle in new[] {0f,2f,15f,30f,-30f,45f})
                    foreach(bool roll in new[] {false,true})
                    {
                        float radians=MathHelper.ToRadians(-angle);
                        Matrix motion=Turn(roll ? Matrix.CreateRotationZ(radians):Matrix.CreateRotationX(radians));
                        var input=CockpitStickMath.Rotation(captured,captured*motion,.08f,false,1,stick.Frame);
                        Vector3 move=Vector3.Zero; Vector2 rotate=Vector2.Zero; float rollRate=0;
                        CockpitStickMath.ApplyFlight(false,true,Vector3.Zero,input,0,0,1,1,ref move,ref rotate,ref rollRate);
                        float command=roll ? input.Z:rotate.X;
                        if(angle==0 && (rotate!=Vector2.Zero || rollRate!=0)) throw new Exception("Capturing a tilted wrist did not start neutral");
                        if(Math.Abs(angle)>=30 && Math.Abs(command)<.999f) throw new Exception("Grab-relative motion lost full travel");
                        if(angle==2 && command!=0) throw new Exception("Tilt deadzone no longer applies to the command");
                        var axes=CockpitStickMath.HandVisual(captured,captured*motion,false,1,stick.Frame);
                        Matrix drawn=(authored*stick.Visual(axes)).GetOrientation();
                        Matrix expected=(authored*Turn(roll ? Matrix.CreateRotationZ(MathHelper.Clamp(radians,-CockpitStickMath.TiltRange,CockpitStickMath.TiltRange)):
                            Matrix.CreateRotationX(MathHelper.Clamp(radians,-CockpitStickMath.TiltRange,CockpitStickMath.TiltRange)))).GetOrientation();
                        if(Vector3.Distance(drawn.Up,expected.Up)>.001f || Vector3.Distance(drawn.Forward,expected.Forward)>.001f)
                            throw new Exception("Held stick hand does not follow the wrist: "+rig.Subtype);
                    }
                }
                Matrix half=Turn(Matrix.CreateRotationX(MathHelper.ToRadians(-15)));
                if(Math.Abs(CockpitStickMath.HandVisual(authored,authored*half,false,2,stick.Frame).X-.5f)>.001f ||
                    Math.Abs(CockpitStickMath.HandVisual(authored,authored*half*half,false,2,stick.Frame).X-.5f)>.001f)
                    throw new Exception("Raised sensitivity does not stop the stick at full command");
                if(Math.Abs(CockpitStickMath.HandVisual(authored,authored*half*half,false,.5f,stick.Frame).X-1)>.001f)
                    throw new Exception("Lowered sensitivity moves the stick past its travel limit");
            }
            Vector3 translated=new Vector3(.7f,.4f,-.5f);
            Vector3 movement=Vector3.Zero; Vector2 rotation=Vector2.Zero; float bank=0;
            CockpitStickMath.ApplyFlight(true,false,translated,Vector3.Zero,.2f,0,1,1,ref movement,ref rotation,ref bank,2,2,.1f);
            if(Vector3.Distance(movement,CockpitStickMath.Response(translated,2)+new Vector3(.1f,.2f,0))>.0001f)
                throw new Exception("Translation response curved twice or affected thumb input");
            float zoom=0; Vector2 aim=Vector2.Zero;
            CockpitStickMath.ApplyTurret(true,true,new Vector3(.5f),translated,1,2,ref aim,ref zoom,.2f);
            if(Math.Abs(aim.X-.25f)>.0001f || Math.Abs(zoom+.3f)>.0001f)
                throw new Exception("Turret response changed zoom curve or thumb input");
            log("PASS grab-relative joystick neutral at arbitrary wrist tilt, full relative travel, wrist-matched stick visuals and independent thumb input");
        }
    }
}
