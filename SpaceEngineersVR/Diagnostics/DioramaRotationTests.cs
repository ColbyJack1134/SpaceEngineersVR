using System;
using SpaceEngineersVR.Player.Control;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class DioramaRotationTests
    {
        private static readonly Vector3D Mid=new Vector3D(0,-.1,-.6);
        private static void Require(bool condition,string message) { if(!condition) throw new Exception(message); }
        private static void Near(Vector3D a,Vector3D b,string message) => Require(Vector3D.Distance(a,b)<1e-8,message);
        private static void Pose(Vector3D axis,double angle,out MatrixD left,out MatrixD right)
        {
            left=right=MatrixD.CreateFromAxisAngle(axis,angle);
            var half=Vector3D.TransformNormal(new Vector3D(.2,0,0),left);
            left.Translation=Mid-half; right.Translation=Mid+half;
        }
        private static Diorama Turn(int hz,Vector3D axis,double rate,out MatrixD left,out MatrixD right)
        {
            var view=new Diorama(); view.Fit(20,MatrixD.Identity,new Vector3D(.1,-.2,-1));
            view.Input(true,0,0); view.Input(true,1,1);
            Pose(axis,0,out left,out right); view.Move(left,right,1d/hz);
            for(int i=1;i<=hz;i++)
            { Pose(axis,rate*i/hz,out left,out right); view.Move(left,right,1d/hz); }
            return view;
        }
        private static double Angle(MatrixD before,MatrixD after)
        {
            var q=QuaternionD.CreateFromRotationMatrix(MatrixD.Transpose(before)*after);
            return 2*Math.Atan2(new Vector3D(q.X,q.Y,q.Z).Length(),Math.Abs(q.W));
        }
        private static void Finish(Diorama view,int hz,MatrixD left,MatrixD right)
        {
            double previous=view.LastRotation;
            for(int i=0;i<2*hz;i++)
            {
                view.Move(left,right,1d/hz);
                Require(view.LastRotation<=previous+1e-12,"Angular glide accelerated");
                previous=view.LastRotation;
                if(i>=hz) Require(!view.Coasting,"Pure angular glide exceeded one second");
            }
            Require(!view.Coasting,"Angular glide did not settle");
        }
        public static void Run(Action<string> log)
        {
            double minimum=double.MaxValue,maximum=0;
            foreach(int hz in new[] {36,72,90})
            {
                foreach(var axis in new[] {Vector3D.Right,Vector3D.Up,Vector3D.Backward,Vector3D.Normalize(Vector3D.One)})
                foreach(double direction in new[] {-.4,.4})
                foreach(bool leftHeld in new[] {false,true})
                for(int frames=0;frames<=3;frames++)
                foreach(double fresh in new[] {direction,0,.02*Math.Sign(direction),-direction})
                {
                    var view=Turn(hz,axis,direction,out var left,out var right);
                    var orientation=view.Orientation;
                    double scale=view.UnitsPerMeter;
                    view.Input(true,leftHeld ? 1:0,leftHeld ? 0:1);
                    for(int i=1;i<=frames;i++)
                    {
                        Pose(axis,direction*(1+i/(double)hz),out left,out right); view.Move(left,right,1d/hz);
                        Require(!view.Coasting,"Rotation coast started with one grip held");
                        Near(view.Orientation.Forward,orientation.Forward,"Rotation continued with one grip held");
                        Near(view.Orientation.Up,orientation.Up,"Twist continued with one grip held");
                    }
                    var anchor=view.Anchor(Vector3D.Zero);
                    var point=Vector3D.Transform(Mid*scale,anchor);
                    var center=view.Center;
                    Pose(axis,direction*(1+frames/(double)hz)+fresh/hz,out left,out right);
                    view.Input(true,0,0); view.Move(left,right,1d/hz);
                    Require(view.Coasting==(fresh==direction),"Angular release ignored fresh motion or lost staggered momentum");
                    Finish(view,hz,left,right);
                    double angle=Angle(orientation,view.Orientation);
                    if(fresh==direction)
                    {
                        Require(angle>.04 && angle<.045,"Gentle rotation coast left its small travel range");
                        var expected=MatrixD.CreateFromAxisAngle(axis,-Math.Sign(direction)*angle)*orientation;
                        Near(view.Orientation.Forward,expected.Forward,"Angular glide reversed direction");
                        Near(view.Orientation.Up,expected.Up,"Angular twist reversed direction");
                        Near(Vector3D.Transform(point,MatrixD.Invert(view.Anchor(Vector3D.Zero)))/scale,Mid,"Angular glide lost the hand-midpoint pivot");
                        minimum=Math.Min(minimum,angle); maximum=Math.Max(maximum,angle);
                    }
                    else
                    {
                        Require(angle<1e-8,"Slow, stationary or reversed release rotated");
                        Near(view.Center,center,"Stopped angular release moved the center");
                    }
                    Require(Math.Abs(view.UnitsPerMeter-scale)<1e-8,"Pure rotation release zoomed");
                }
                Cancellations(hz);
                SplitHistory(hz);
                CombinedRelease(hz);
                var fast=Turn(hz,Vector3D.Up,4,out var a,out var b);
                var before=fast.Orientation;
                Pose(Vector3D.Up,4*(1+1d/hz),out a,out b);
                fast.Input(true,0,0); fast.Move(a,b,1d/hz); Finish(fast,hz,a,b);
                Require(Angle(before,fast.Orientation)<.084,"Fast angular release exceeded the travel cap");
                log($"PASS angular release {hz} Hz: {minimum*180/Math.PI:F2} deg gentle coast; all axes, both grip orders, 0-3 split frames, fresh-motion stops, pivot, cap and braking.");
            }
            Require(maximum-minimum<1e-8,"Angular coast changed with render cadence");
        }
        private static void Cancellations(int hz)
        {
            for(int reason=0;reason<11;reason++)
            {
                var view=Turn(hz,Vector3D.Up,.4,out var left,out var right);
                Pose(Vector3D.Up,.4*(1+1d/hz),out left,out right);
                view.Input(true,0,0); view.Move(left,right,1d/hz);
                Require(view.Coasting,"Angular cancellation fixture did not coast");
                switch(reason)
                {
                    case 0: view.Input(true,.1f,0); break;
                    case 1: view.Input(true,1,1); break;
                    case 2: view.Input(true,0,0,true); break;
                    case 3: view.Input(false,0,0); break;
                    case 4: view.Cancel(); break;
                    case 5: view.ChangeReference(MatrixD.Identity,MatrixD.CreateRotationY(.2)); break;
                    case 6: view.Rebase(Matrix.Identity,Matrix.CreateTranslation(0,.1f,0)); break;
                    case 7: view.Fit(20,MatrixD.Identity,Vector3D.Forward); break;
                    case 8: view.Move(left,right,.2); break;
                    case 9: left.Translation=new Vector3D(double.NaN,0,0); view.Move(left,right,1d/hz); break;
                    case 10: view.Move(left,right,double.NaN); break;
                }
                var orientation=view.Orientation; var center=view.Center;
                Require(!view.Coasting,"Cancellation retained angular coast: "+reason);
                Finish(view,hz,left,right);
                Near(view.Orientation.Forward,orientation.Forward,"Cancellation replayed rotation: "+reason);
                Near(view.Center,center,"Cancellation replayed orbit center: "+reason);
            }
        }
        private static void SplitHistory(int hz)
        {
            for(int reason=0;reason<7;reason++)
            {
                var view=Turn(hz,Vector3D.Up,.4,out var left,out var right);
                view.Input(true,1,0);
                double time=1+1d/hz;
                Pose(Vector3D.Up,.4*time,out left,out right); view.Move(left,right,1d/hz);
                switch(reason)
                {
                    case 0: view.Move(left,right,1d/hz); break;
                    case 1: time-=1d/hz; Pose(Vector3D.Up,.4*time,out left,out right); view.Move(left,right,1d/hz); break;
                    case 2:
                        for(int i=0;i<hz/4;i++)
                        { time+=1d/hz; Pose(Vector3D.Up,.4*time,out left,out right); view.Move(left,right,1d/hz); }
                        break;
                    case 3: view.Input(true,1,1); view.Move(left,right,1d/hz); break;
                    case 4: view.Cancel(); break;
                    case 5: view.Move(left,right,.2); break;
                    case 6: right.Translation+=Vector3D.One; view.Move(left,right,1d/hz);
                        Require(!view.Held,"Released controller tracking jump survived angular history"); break;
                }
                var orientation=view.Orientation;
                Pose(Vector3D.Up,.4*(time+1d/hz),out left,out right);
                view.Input(true,0,0); view.Move(left,right,1d/hz);
                for(int i=0;i<2*hz;i++) view.Move(left,right,1d/hz);
                Near(view.Orientation.Forward,orientation.Forward,"Angular history survived stop, reversal, expiry or cancellation: "+reason);
            }
        }
        private static void MixedPose(double panTime,double zoomTime,double rotationTime,out MatrixD left,out MatrixD right)
        {
            Pose(Vector3D.Up,.4*rotationTime,out left,out right);
            var shift=new Vector3D(.3*panTime,0,0);
            left.Translation=Mid+(left.Translation-Mid)*Math.Exp(.6*zoomTime)+shift;
            right.Translation=Mid+(right.Translation-Mid)*Math.Exp(.6*zoomTime)+shift;
        }
        private static void CombinedRelease(int hz)
        {
            foreach(bool keepZoom in new[] {false,true})
            foreach(bool keepRotation in new[] {false,true})
            {
                var view=new Diorama(); view.Fit(20,MatrixD.Identity,new Vector3D(.1,-.2,-1));
                view.Input(true,0,0); view.Input(true,1,1);
                MixedPose(0,0,0,out var left,out var right); view.Move(left,right,1d/hz);
                for(int i=1;i<=hz;i++)
                {
                    double time=i/(double)hz;
                    MixedPose(time,time,time,out left,out right); view.Move(left,right,1d/hz);
                }
                var before=view.Orientation; double scale=view.UnitsPerMeter;
                view.Input(true,1,0);
                for(int i=1;i<=2;i++)
                {
                    double time=1+i/(double)hz;
                    MixedPose(time,keepZoom ? time:1,keepRotation ? time:1,out left,out right);
                    view.Move(left,right,1d/hz);
                }
                // Fresh motion must not revive a component rejected during the split release.
                MixedPose(1+3d/hz,(keepZoom ? 1+2d/hz:1)+1d/hz,(keepRotation ? 1+2d/hz:1)+1d/hz,out left,out right);
                view.Input(true,0,0); view.Move(left,right,1d/hz);
                MatrixD stopped=MatrixD.Identity;
                for(int i=0;i<2*hz;i++)
                {
                    view.Move(left,right,1d/hz);
                    if(i==hz) { stopped=view.Orientation; if(keepZoom) Require(view.Coasting,"Angular expiry stopped zoom coast early"); }
                }
                Require(!view.Coasting,"Combined glide did not settle");
                Near(view.Orientation.Forward,stopped.Forward,"Rotation continued with the longer pan/zoom decay");
                double angle=Angle(before,view.Orientation);
                Require(keepRotation ? angle>.04 && angle<.045 : angle<1e-8,"Zoom history revived or suppressed angular glide");
                Require(keepZoom ? scale/view.UnitsPerMeter>1.42 : view.UnitsPerMeter==scale,"Angular history revived or suppressed zoom glide");
            }
        }
    }
}
