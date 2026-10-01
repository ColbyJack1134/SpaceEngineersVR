using System;
using System.IO;
using System.Xml.Serialization;
using SpaceEngineersVR.Config;
using SpaceEngineersVR.Player.Control;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class DioramaGestureTests
    {
        private static readonly Vector3D Mid=new Vector3D(0,-.1,-.6),Center=new Vector3D(.1,-.2,-1);
        private static void Require(bool condition,string message) { if(!condition) throw new Exception(message); }
        private static void Near(Vector3D a,Vector3D b,string message,double tolerance=1e-8) =>
            Require(Vector3D.Distance(a,b)<tolerance,message);
        private static void Pose(MatrixD turn,Vector3D shift,double size,out MatrixD left,out MatrixD right)
        {
            left=right=turn;
            var half=Vector3D.TransformNormal(new Vector3D(.2*size,0,0),turn);
            left.Translation=Mid+shift-half; right.Translation=Mid+shift+half;
        }
        private static Diorama Start(int hz)
        {
            var view=new Diorama(); view.Fit(20,MatrixD.Identity,Center);
            view.Input(true,0,0); view.Input(true,1,1);
            Pose(MatrixD.Identity,Vector3D.Zero,1,out var left,out var right);
            view.Move(left,right,1d/hz);
            return view;
        }
        public static void Run(Action<string> log)
        {
            foreach(int hz in new[] {36,72,90})
            {
                CombinedMotion(hz);
                double noise=RotationNoise(hz);
                log($"PASS free gestures {hz} Hz: simultaneous 3D pan, zoom and full-axis rotation; held rotation noise RMS {noise*180/Math.PI:F3} deg.");
            }
            var serializer=new XmlSerializer(typeof(PluginConfig));
            foreach(bool oldSetting in new[] {true,false})
            {
                string xml="<PluginConfig><ThirdPersonMode>2</ThirdPersonMode><ThirdPersonGestureLock>"+(oldSetting ? "true":"false")+"</ThirdPersonGestureLock></PluginConfig>";
                var saved=(PluginConfig)serializer.Deserialize(new StringReader(xml));
                Require(saved.ThirdPersonMode==2,"Removing gesture separation changed the saved camera mode");
                using(var writer=new StringWriter())
                {
                    serializer.Serialize(writer,saved);
                    Require(!writer.ToString().Contains("ThirdPersonGesture"),"Removed gesture preference was saved again");
                }
            }
        }
        private static void CombinedMotion(int hz)
        {
            foreach(var axis in new[] {Vector3D.Right,Vector3D.Up,Vector3D.Backward,Vector3D.Normalize(Vector3D.One)})
            {
                var view=Start(hz); var rotateOnly=Start(hz);
                for(int i=1;i<=hz;i++)
                {
                    double t=i/(double)hz;
                    var turn=MatrixD.CreateFromAxisAngle(axis,.5*t);
                    Pose(turn,new Vector3D(.15,.1,-.05)*t,Math.Exp(.25*t),out var left,out var right);
                    view.Move(left,right,1d/hz);
                    Pose(turn,Vector3D.Zero,1,out left,out right); rotateOnly.Move(left,right,1d/hz);
                    if(i==1) Require(view.LastTranslation>0 && view.LastRotation>0 && view.UnitsPerMeter<25,"Free gesture waited for intent selection");
                }
                Require(view.UnitsPerMeter<20 && Vector3D.Distance(view.Center,rotateOnly.Center)>.1,"Combined gesture suppressed pan or zoom");
                Near(view.Orientation.Forward,rotateOnly.Orientation.Forward,"Pan/zoom contaminated rotation");
                Near(view.Orientation.Up,rotateOnly.Orientation.Up,"Pan/zoom contaminated twist");
                var q=QuaternionD.CreateFromRotationMatrix(view.Orientation);
                double angle=2*Math.Acos(Math.Abs(q.W));
                Require(angle>.6 && angle<.75,"Rotation response left the reduced sensitivity range");
            }
        }
        private static double RotationNoise(int hz)
        {
            double worst=0;
            foreach(double span in new[] {.12,.4})
            foreach(double panSpeed in new[] {0d,.5})
            {
                var clean=Start(hz); var noisy=Start(hz);
                Pose(MatrixD.Identity,Vector3D.Zero,span/.4,out var left,out var right);
                // Rebase the hand span before measuring independent positional and wrist noise.
                clean.Input(true,1,0); noisy.Input(true,1,0);
                clean.Move(left,right,1d/hz); noisy.Move(left,right,1d/hz);
                clean.Input(true,1,1); noisy.Input(true,1,1);
                clean.Move(left,right,1d/hz); noisy.Move(left,right,1d/hz);
                double sum=0;
                for(int i=1;i<=2*hz;i++)
                {
                    double t=i/(double)hz;
                    var turn=MatrixD.CreateRotationY(.4*t);
                    var shift=new Vector3D(panSpeed*t,0,0);
                    Pose(turn,shift,span/.4,out left,out right); clean.Move(left,right,1d/hz);
                    var wrist=MatrixD.CreateRotationX(.02*Math.Sin(t*2*Math.PI*11));
                    left=wrist*left; right=wrist*right;
                    left.Translation+=new Vector3D(0,.0015*Math.Sin(t*2*Math.PI*13),.0015*Math.Sin(t*2*Math.PI*9));
                    right.Translation-=new Vector3D(0,.0015*Math.Sin(t*2*Math.PI*13),.0015*Math.Sin(t*2*Math.PI*9));
                    noisy.Move(left,right,1d/hz);
                    var error=QuaternionD.CreateFromRotationMatrix(MatrixD.Transpose(clean.Orientation)*noisy.Orientation);
                    double angle=2*Math.Acos(Math.Min(1,Math.Abs(error.W)));
                    sum+=angle*angle;
                }
                double rms=Math.Sqrt(sum/(2*hz)); worst=Math.Max(worst,rms);
                Require(rms<.01,"Held rotation admitted excessive independent controller noise: "+rms);
                for(int i=0;i<hz;i++)
                {
                    Pose(MatrixD.CreateRotationY(.8),new Vector3D(panSpeed*2,0,0),span/.4,out left,out right);
                    clean.Move(left,right,1d/hz);
                }
                Near(clean.Orientation.Forward,MatrixD.CreateRotationY(-1.16).Forward,"Rotation filter lost deliberate slow movement",.00001);
            }
            return worst;
        }
    }
}
