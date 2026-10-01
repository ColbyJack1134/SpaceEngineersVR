using System;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Player.Control;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class ThirdPersonTests
    {
        private static void Require(bool condition,string message) { if(!condition) throw new Exception(message); }
        private static void Near(Vector3D a,Vector3D b,string message,double tolerance=.00002) =>
            Require(Vector3D.Distance(a,b)<tolerance,message+": "+a+" / "+b);
        private static bool Step(Diorama view,bool available,float a,float b,Vector3D l,Vector3D r)
        {
            view.Input(available,a,b);
            view.Move(MatrixD.CreateTranslation(l),MatrixD.CreateTranslation(r),0);
            return view.Held;
        }
        public static void Run(Action<string> log)
        {
            var target=new Vector3D(2e8,-3e8,4e8);
            var origin=Matrix.CreateRotationY(.37f)*Matrix.CreateTranslation(1.2f,1.6f,-.4f);
            var head=Matrix.CreateRotationX(.23f)*Matrix.CreateTranslation(.1f,-.05f,.2f)*origin;
            var orientation=MatrixD.CreateFromYawPitchRoll(.7,.2,-.1);
            foreach(double scale in new[] {.1,1,25,1000,100000})
            {
                var anchor=orientation; anchor.Translation=target;
                var left=MatrixD.Invert(VrMath.EyeView(MatrixD.Invert(anchor),head,Matrix.Invert(origin),Matrix.CreateTranslation(-.032f,0,0),scale));
                var right=MatrixD.Invert(VrMath.EyeView(MatrixD.Invert(anchor),head,Matrix.Invert(origin),Matrix.CreateTranslation(.032f,0,0),scale));
                Require(Math.Abs(Vector3D.Distance(left.Translation,right.Translation)/scale-.064)<.00002,"Miniature stereo separation changed in physical meters");
                var local=VrMath.Affine(head*Matrix.Invert(origin));
                Near(((left.Translation+right.Translation)*.5-target)/scale,Vector3D.TransformNormal(local.Translation,orientation),"Scaled head movement mismatched stereo scale");
                Near(left.Up,(local*orientation).Up,"World scaling tilted the eyes");
                var packet=new CameraRig.Frame(anchor,Matrix.Invert(origin),-1,scale,true);
                Near(Vector3D.Transform(head.Translation,packet.TrackingToWorld),(left.Translation+right.Translation)*.5,"Radial and eye transforms disagree",.2);
            }
            var view=new Diorama();
            Vector3D l=new Vector3D(-.2,-.2,-.5),r=new Vector3D(.2,-.2,-.5);
            view.Fit(20,orientation,new Vector3D(.1,-.2,-1));
            Require(!Step(view,true,1,1,l,r),"Already-held grips acquired on entry");
            Step(view,true,0,0,l,r);
            Require(!Step(view,true,1,0,l,r),"A normal descent became view movement");
            Require(Step(view,true,1,1,l,r),"Deliberate two-grip capture failed");
            var before=view.Anchor(target);
            var point=Vector3D.Transform((l+r)*.5*view.UnitsPerMeter,before);
            double oldScale=view.UnitsPerMeter;
            var mid=(l+r)*.5;
            var turn=MatrixD.CreateRotationY(.3);
            var shift=new Vector3D(.03,.01,-.02);
            var nl=Vector3D.TransformNormal(l-mid,turn)*1.5+mid+shift;
            var nr=Vector3D.TransformNormal(r-mid,turn)*1.5+mid+shift;
            Step(view,true,1,1,nl,nr);
            Require(Math.Abs(view.UnitsPerMeter-oldScale/1.5)<.00001,"Hands apart did not enlarge the ship");
            Near(Vector3D.Transform(point,MatrixD.Invert(view.Anchor(target)))/view.UnitsPerMeter,mid+shift,"Two-hand manipulation lost its pivot");
            Near(view.Orientation.Up,orientation.Up,"Gesture introduced camera pitch/roll");
            var oldCenter=view.Center;
            Step(view,true,1,0,nl,nr); Near(view.Center,oldCenter,"Dropping a hand moved the scene");
            Step(view,true,1,0,nl+shift,nr); Near(view.Center,oldCenter+shift,"Single-hand drag failed");
            Step(view,true,.2f,0,nl+shift,nr); Require(view.Held,"Partial grip release dropped capture");
            Step(view,true,0,0,nl+shift,nr); oldCenter=view.Center;
            Step(view,true,0,0,l,r); Near(view.Center,oldCenter,"Released view drifted");
            Step(view,true,1,1,l,r); Step(view,false,1,1,l,r);
            Require(!Step(view,true,1,1,l,r),"Tracking/focus return reacquired held grips");
            Step(view,true,0,0,l,r); Step(view,true,1,1,l,r);
            Require(!Step(view,true,1,1,l+Vector3D.One,r),"Tracking jump moved the world");
            Require(!Step(view,true,1,1,l,r),"Tracking jump rearmed without release");
            view.Cancel();
            var oldAnchor=view.Anchor(target);
            var newOrigin=Matrix.CreateRotationY(-.8f)*Matrix.CreateTranslation(-.3f,1.4f,.2f);
            var oldEye=MatrixD.Invert(VrMath.EyeView(MatrixD.Invert(oldAnchor),head,Matrix.Invert(origin),Matrix.Identity,view.UnitsPerMeter));
            view.Rebase(origin,newOrigin);
            var newEye=MatrixD.Invert(VrMath.EyeView(MatrixD.Invert(view.Anchor(target)),head,Matrix.Invert(newOrigin),Matrix.Identity,view.UnitsPerMeter));
            Near(oldEye.Translation,newEye.Translation,"Recenter jumped before the fade",.0001);
            Near(oldEye.Forward,newEye.Forward,"Recenter rotated before the fade");
            var moved=view.Anchor(target+new Vector3D(100,20,-10));
            Near(moved.Translation-view.Anchor(target).Translation,new Vector3D(100,20,-10),"Ship translation lagged or changed scale");
            var message=new object(); var frame=new CameraRig.Frame(moved,Matrix.Identity,-20,view.UnitsPerMeter,true);
            RenderFrameBridge.Capture(message,frame); RenderFrameBridge.Consume(message);
            Require(ReferenceEquals(frame,RenderFrameBridge.ForCurrentOwner(frame)),"Third-person render packet lost scale/owner");
            Require(RenderFrameBridge.ForCurrentOwner(null)==null,"Third-person packet survived exit");
            RotationAndFollow(log);
            RenderCadence(log);
            DioramaMotionTests.Run(log);
            log("PASS third-person stereo/head scale, large-world precision, two/one-hand pivot capture, partial release, no drift, tracking cancellation, recenter continuity and render handoff.");
        }
        private static MatrixD MovedHand(MatrixD hand,Vector3D pivot,MatrixD rotation,double scale,Vector3D shift)
        {
            var result=hand.GetOrientation()*rotation;
            result.Translation=Vector3D.TransformNormal(hand.Translation-pivot,rotation)*scale+pivot+shift;
            return result;
        }
        private static void RotationAndFollow(Action<string> log)
        {
            var l=MatrixD.CreateRotationY(.3)*MatrixD.CreateTranslation(-.2,-.1,-.55);
            var r=MatrixD.CreateRotationZ(-.4)*MatrixD.CreateTranslation(.2,-.1,-.55);
            var mid=(l.Translation+r.Translation)*.5;
            var center=new Vector3D(.1,-.2,-1);
            var target=new Vector3D(2e8,-3e8,4e8);
            var initial=MatrixD.CreateFromYawPitchRoll(.2,-.1,.15);
            var shift=new Vector3D(.01,.02,-.03);
            foreach(var rotation in new[] {MatrixD.CreateRotationX(.3),MatrixD.CreateRotationY(-.4),MatrixD.CreateRotationZ(.25),MatrixD.CreateFromYawPitchRoll(.3,-.2,.1)})
            {
                var view=new Diorama(); view.Fit(20,initial,center);
                view.Input(true,0,0); view.Input(true,1,1); view.Move(l,r,0);
                double scale=view.UnitsPerMeter;
                var pivot=Vector3D.Transform(mid*scale,view.Anchor(target));
                view.Move(MovedHand(l,mid,rotation,1.2,shift),MovedHand(r,mid,rotation,1.2,shift),0);
                Near(view.Orientation.Forward,(MatrixD.Transpose(rotation)*initial).Forward,"Two-hand full rotation lost pitch/yaw");
                Near(view.Orientation.Up,(MatrixD.Transpose(rotation)*initial).Up,"Two-hand full rotation lost roll/twist");
                Near(Vector3D.Transform(pivot,MatrixD.Invert(view.Anchor(target)))/view.UnitsPerMeter,mid+shift,"Full orbit moved its grabbed pivot");
                Require(Math.Abs(view.UnitsPerMeter-scale/1.2)<.00002,"Full orbit changed pinch scale: "+view.UnitsPerMeter+" / "+scale/1.2);
                view.Input(true,0,0); var anchor=view.Anchor(target);
                for(int i=0;i<90;i++) view.Move(l,r,1d/90);
                Near(view.Anchor(target).Translation,anchor.Translation,"Released rotation kept moving");
            }
            var follow=new ObserverFollow(); follow.Reset(MatrixD.Identity,Vector3D.Up);
            var ship=MatrixD.CreateFromYawPitchRoll(.4,.3,-.25);
            follow.Advance(ship);
            Near(follow.Reference(ObserverMode.Ship).Forward,ship.Forward,"Full follow lost ship pitch/yaw");
            Near(follow.Reference(ObserverMode.Ship).Up,ship.Up,"Full follow lost ship roll");
            Near(follow.Reference(ObserverMode.Fixed).Forward,Vector3D.Forward,"Fixed observer followed rotation");
            Near(follow.Reference(ObserverMode.Heading).Up,Vector3D.Up,"Heading reference tilted");
            Require(Vector3D.Distance(follow.Reference(ObserverMode.Heading).Forward,Vector3D.Forward)>.1,"Heading reference ignored ship turn");
            foreach(ObserverMode from in Enum.GetValues(typeof(ObserverMode)))
            foreach(ObserverMode to in Enum.GetValues(typeof(ObserverMode)))
            {
                var view=new Diorama(); view.Fit(20,initial,center);
                var a=follow.Reference(from); var b=follow.Reference(to);
                var before=view.Anchor(target,a);
                view.ChangeReference(a,b);
                var after=view.Anchor(target,b);
                Near(before.Translation,after.Translation,"Cycling camera mode jumped",.000001);
                Near(before.Forward,after.Forward,"Cycling camera mode rotated");
                Near(before.Up,after.Up,"Cycling camera mode rolled");
            }
            follow.Reset(MatrixD.Identity,Vector3D.Up);
            for(int i=1;i<=1440;i++)
            {
                follow.Advance(MatrixD.CreateRotationX(i*Math.PI/360));
                Near(follow.Reference(ObserverMode.Heading).Forward,Vector3D.Forward,"Pitch loop flipped heading at a pole");
                Near(follow.Reference(ObserverMode.Heading).Up,Vector3D.Up,"Heading pole lost vertical");
            }
            follow.Reset(MatrixD.Identity,Vector3D.Up);
            for(int i=1;i<=1440;i++)
            {
                follow.Advance(MatrixD.CreateRotationZ(i*Math.PI/360));
                Near(follow.Reference(ObserverMode.Heading).Forward,Vector3D.Forward,"Roll loop changed heading");
            }
            var defaults=new Config.PluginConfig();
            Require(defaults.ThirdPersonMode==0,"New camera mode must default to full ship follow");
            defaults.ThirdPersonMode=9; Require(defaults.ThirdPersonMode==0,"Invalid saved camera mode accepted");
            var jump=new Diorama(); jump.Fit(20,MatrixD.Identity,center);
            jump.Input(true,0,0); jump.Input(true,1,1); jump.Move(l,r,0);
            var flipped=l; flipped=MatrixD.CreateRotationX(Math.PI)*flipped;
            Require(!jump.Move(flipped,r,1d/90) && !jump.Held,"Angular tracking discontinuity threw the world");
            Require(!jump.Input(true,1,1),"Angular tracking cancellation rearmed held grips");
            log("PASS observer modes: full ship/heading/fixed, retained full-axis gesture offset, all nine mode transitions without a jump, pitch/roll loops and ship-follow default.");
        }
        private static void RenderCadence(Action<string> log)
        {
            var a=MatrixD.CreateTranslation(-.2,-.1,-.55); var b=MatrixD.CreateTranslation(.2,-.1,-.55);
            var fresh=new Diorama(); var delayed=new Diorama();
            foreach(var v in new[] {fresh,delayed}) { v.Fit(20,MatrixD.Identity,new Vector3D(0,0,-1)); v.Input(true,0,0); v.Input(true,1,1); v.Move(a,b,0); }
            int freshStalls=0,delayedStalls=0;
            double worstStepError=0;
            var oldFresh=fresh.Center; var oldDelayed=delayed.Center;
            for(int frame=1;frame<=180;frame++)
            {
                double time=frame/90d,mainTime=Math.Floor(frame*60d/90)/60;
                var l=a; var r=b; l.Translation+=new Vector3D(time*.2,0,0); r.Translation+=new Vector3D(time*.2,0,0);
                fresh.Move(l,r,1d/90);
                l=a; r=b; l.Translation+=new Vector3D(mainTime*.2,0,0); r.Translation+=new Vector3D(mainTime*.2,0,0);
                delayed.Move(l,r,0);
                double step=fresh.Center.X-oldFresh.X;
                if(frame>45)
                {
                    if(step<1e-6) freshStalls++;
                    if(delayed.Center.X-oldDelayed.X<1e-6) delayedStalls++;
                    worstStepError=Math.Max(worstStepError,Math.Abs(step-.24/90));
                }
                oldFresh=fresh.Center; oldDelayed=delayed.Center;
            }
            Require(freshStalls==0 && delayedStalls>40 && worstStepError<.00001,"Fresh-pose drag still has simulation-cadence steps");
            var stoppedLeft=a; var stoppedRight=b;
            stoppedLeft.Translation+=new Vector3D(.4,0,0); stoppedRight.Translation+=new Vector3D(.4,0,0);
            fresh.Input(true,0,0); var stopped=fresh.Center;
            fresh.Move(stoppedLeft,stoppedRight,1d/90);
            for(int i=0;i<90;i++) fresh.Move(a,b,1d/90);
            Near(fresh.Center,stopped,"Stationary release caught up with the filter");
            var noise=new Diorama(); noise.Fit(20,MatrixD.Identity,new Vector3D(0,0,-1));
            noise.Input(true,0,0); noise.Input(true,1,1); noise.Move(a,b,0);
            double filteredNoise=0,rawNoise=0; var previous=noise.Center;
            for(int i=0;i<240;i++)
            {
                double jitter=i%2==0 ? .002 : -.002;
                var l=a; var r=b; l.Translation+=new Vector3D(jitter,0,0); r.Translation+=new Vector3D(jitter,0,0);
                noise.Move(l,r,1d/90);
                if(i>20) { filteredNoise+=Math.Pow(noise.Center.X-previous.X,2); rawNoise+=.004*.004; }
                previous=noise.Center;
            }
            Require(filteredNoise<rawNoise*.4,"Gesture filter did not attenuate hand noise");
            log($"PASS drag replay at 90 Hz render / 60 Hz simulation: old pose path stalled {delayedStalls} frames; fresh path {freshStalls}; noise RMS ratio {Math.Sqrt(filteredNoise/rawNoise):F2}; stationary release without drift. Replay does not establish headset comfort.");
        }
    }
}
