using System;
using SpaceEngineersVR.Player.Control;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class DioramaMotionTests
    {
        private static void Require(bool condition,string message) { if(!condition) throw new Exception(message); }
        private static void Near(Vector3D a,Vector3D b,string message,double tolerance=1e-8) =>
            Require(Vector3D.Distance(a,b)<tolerance,message);
        private static void Hands(double time,double pan,double zoom,out MatrixD left,out MatrixD right)
        {
            var mid=new Vector3D(time*pan,-.1,-.6);
            var span=new Vector3D(.2*Math.Exp(time*zoom),0,0);
            left=MatrixD.CreateTranslation(mid-span); right=MatrixD.CreateTranslation(mid+span);
        }
        private static Diorama Drag(int hz,double pan,double zoom,out MatrixD left,out MatrixD right)
        {
            var view=new Diorama(); view.Fit(20,MatrixD.Identity,new Vector3D(0,-.1,-1));
            view.Input(true,0,0); view.Input(true,1,1);
            Hands(0,pan,zoom,out left,out right); view.Move(left,right,1d/hz);
            for(int i=1;i<=hz;i++) { Hands(i/(double)hz,pan,zoom,out left,out right); view.Move(left,right,1d/hz); }
            return view;
        }
        private static void LetGo(Diorama view,int hz,double pan,double zoom,out MatrixD left,out MatrixD right)
        {
            view.Input(true,0,0);
            Hands(1+1d/hz,pan,zoom,out left,out right); view.Move(left,right,1d/hz);
        }
        private static void Finish(Diorama view,int hz,MatrixD left,MatrixD right)
        {
            for(int i=0;i<2*hz;i++) view.Move(left,right,1d/hz);
            Require(!view.Coasting,"Release motion did not settle");
        }
        public static void Run(Action<string> log)
        {
            double minPan=double.MaxValue,maxPan=0,minZoom=double.MaxValue,maxZoom=0;
            foreach(int hz in new[] {36,72,90})
            {
                var pan=Drag(hz,.3,0,out var left,out var right);
                double heldPan=pan.Center.X,scale=pan.UnitsPerMeter;
                Require(heldPan>.33 && heldPan<.36,"Pan response lost the increased sensitivity or gained excessive lag");
                LetGo(pan,hz,.3,0,out left,out right);
                Require(pan.Coasting,"Moving pan release did not glide");
                double previous=pan.Center.X,lastStep=double.MaxValue;
                for(int i=0;i<2*hz;i++)
                {
                    pan.Move(left,right,1d/hz);
                    double step=pan.Center.X-previous;
                    Require(step>=-1e-12 && step<=lastStep+1e-12,"Pan coast accelerated or reversed");
                    previous=pan.Center.X; lastStep=step;
                }
                double distance=pan.Center.X-heldPan;
                Require(!pan.Coasting && distance>.09 && distance<.10,"Pan coast distance/duration exceeded the longer glide");
                Require(pan.UnitsPerMeter==scale,"Pan release changed scale");
                Near(pan.Orientation.Forward,Vector3D.Forward,"Pan coast rotated the view");
                var stopped=pan.Center; Finish(pan,hz,left,right); Near(pan.Center,stopped,"Settled pan drifted");
                minPan=Math.Min(minPan,pan.Center.X); maxPan=Math.Max(maxPan,pan.Center.X);

                var zoom=Drag(hz,0,.6,out left,out right);
                double heldScale=zoom.UnitsPerMeter;
                var pivot=(left.Translation+right.Translation)*.5;
                var offset=(zoom.Center-pivot)*heldScale;
                Require(25/heldScale>2.05 && 25/heldScale<2.2,"Zoom response lost precision gain or stalled");
                LetGo(zoom,hz,0,.6,out left,out right);
                Require(zoom.Coasting,"Moving zoom release did not glide");
                Finish(zoom,hz,left,right);
                double extra=heldScale/zoom.UnitsPerMeter;
                Require(extra>1.42 && extra<1.45,"Zoom coast exceeded the longer continuation");
                Near((zoom.Center-pivot)*zoom.UnitsPerMeter,offset,"Zoom coast moved the hand-midpoint anchor");
                Near(zoom.Orientation.Up,Vector3D.Up,"Zoom coast rotated the view");
                minZoom=Math.Min(minZoom,zoom.UnitsPerMeter); maxZoom=Math.Max(maxZoom,zoom.UnitsPerMeter);

                foreach(double slow in new[] {0,.02})
                {
                    var stop=Drag(hz,.3,.6,out left,out right);
                    var center=stop.Center; double units=stop.UnitsPerMeter;
                    var mid=(left.Translation+right.Translation)*.5;
                    var half=(right.Translation-left.Translation)*.5*Math.Exp(slow/hz);
                    mid+=new Vector3D(slow/hz,0,0);
                    left.Translation=mid-half; right.Translation=mid+half;
                    stop.Input(true,0,0); stop.Move(left,right,1d/hz); Finish(stop,hz,left,right);
                    Near(stop.Center,center,"Slow release continued pending pan/zoom smoothing");
                    Require(stop.UnitsPerMeter==units,"Slow release continued zoom");
                }
                RegrabAndTransitions(hz);
                StaggeredReleases(hz);
                foreach(bool zooming in new[] {false,true})
                {
                    double speed=zooming ? .12:.06;
                    var gentle=Drag(hz,zooming ? 0:speed,zooming ? speed:0,out left,out right);
                    LetGo(gentle,hz,zooming ? 0:speed,zooming ? speed:0,out left,out right);
                    Require(gentle.Coasting,"Gentle moving release did not start a glide");
                    Finish(gentle,hz,left,right);
                }
                RotationAndReversal(hz);
                Cancellations(hz);
                NoiseAndLimits(hz);
                log($"PASS pan/zoom motion {hz} Hz: pan coast {distance*100:F2} cm; zoom coast {(extra-1)*100:F2}%; slow/still release, regrab, hand changes, flight neutralization and cancellation.");
            }
            Require(maxPan-minPan<.002,"Pan response changed substantially with frame cadence");
            Require(maxZoom/minZoom<1.003,"Zoom response changed substantially with frame cadence");
            log("PASS independent pan/zoom response and bounded release motion at 36/72/90 Hz. Synthetic replay does not establish headset comfort.");
            DioramaGestureTests.Run(log);
        }
        private static void RegrabAndTransitions(int hz)
        {
            foreach(float pressure in new[] {.1f,1f})
            {
                var view=Drag(hz,.3,.6,out var left,out var right);
                LetGo(view,hz,.3,.6,out left,out right);
                var center=view.Center; double scale=view.UnitsPerMeter;
                view.Input(true,pressure,pressure==1 ? 1:0);
                Require(!view.Coasting && (pressure<1 || view.Held),"New grip did not brake/reacquire");
                for(int i=0;i<2*hz;i++) view.Move(left,right,1d/hz);
                Near(view.Center,center,"Regrab replayed old pan/zoom velocity");
                Require(view.UnitsPerMeter==scale,"Regrab replayed old zoom velocity");
            }
            var transition=Drag(hz,.3,.6,out var l,out var r);
            var before=transition.Center; double units=transition.UnitsPerMeter;
            transition.Input(true,1,0); transition.Move(l,r,1d/hz);
            Near(transition.Center,before,"Dropping a hand jumped the view");
            Require(!transition.Coasting,"Dropping one hand started a coast");
            for(int i=0;i<hz;i++) { l.Translation+=new Vector3D(.3/hz,0,0); transition.Move(l,r,1d/hz); }
            Require(transition.UnitsPerMeter==units,"One-hand pan retained zoom velocity");
            before=transition.Center;
            transition.Input(true,1,1); transition.Move(l,r,1d/hz);
            Near(transition.Center,before,"Adding a hand jumped the view");
            transition.Input(true,0,0); transition.Move(l,r,1d/hz);
            Require(!transition.Coasting,"Hand transition seeded release momentum");

            var split=Drag(hz,0,.6,out l,out r);
            split.Input(true,1,0);
            LetGo(split,hz,0,.6,out l,out r);
            Require(split.Coasting,"Grips released between render samples lost zoom momentum");

            var single=Drag(hz,0,0,out l,out r);
            single.Input(true,0,1); single.Move(l,r,1d/hz);
            for(int i=0;i<hz;i++) { r.Translation+=new Vector3D(0,.3/hz,0); single.Move(l,r,1d/hz); }
            units=single.UnitsPerMeter; before=single.Center;
            single.Input(true,0,0); r.Translation+=new Vector3D(0,.3/hz,0); single.Move(l,r,1d/hz);
            Require(single.Coasting,"Retained single-hand pan did not coast on release");
            Finish(single,hz,l,r);
            Require(single.Center.Y>before.Y && single.UnitsPerMeter==units,"One-hand release added zoom or lost pan direction");
        }
        private static void StaggeredReleases(int hz)
        {
            foreach(bool leftHeld in new[] {false,true})
            foreach(double direction in new[] {-.6,.6})
            {
                for(int frames=0;frames<=3;frames++)
                foreach(double rate in new[] {direction,0,.02*Math.Sign(direction),-direction})
                {
                    var view=Drag(hz,0,direction,out var left,out var right);
                    double scale=view.UnitsPerMeter;
                    view.Input(true,leftHeld ? 1:0,leftHeld ? 0:1);
                    for(int i=1;i<=frames;i++)
                    {
                        Hands(1+i/(double)hz,0,direction,out left,out right); view.Move(left,right,1d/hz);
                        Require(!view.Coasting && view.UnitsPerMeter==scale,"Zoom continued while one hand remained held");
                    }
                    Hands(1+(frames+rate/direction)/hz,0,direction,out left,out right);
                    view.Input(true,0,0); view.Move(left,right,1d/hz); Finish(view,hz,left,right);
                    double travel=Math.Log(scale/view.UnitsPerMeter)*Math.Sign(direction);
                    if(rate==direction) Require(travel>.33 && travel<.38,"Brief staggered release lost or amplified zoom glide");
                    else Require(view.UnitsPerMeter==scale,"Slow, stopped or reversed split release flung the zoom");
                }
                for(int reason=0;reason<10;reason++)
                {
                    var view=Drag(hz,0,direction,out var left,out var right);
                    view.Input(true,leftHeld ? 1:0,leftHeld ? 0:1);
                    Hands(1+1d/hz,0,direction,out left,out right); view.Move(left,right,1d/hz);
                    double time=1+1d/hz;
                    switch(reason)
                    {
                        case 0: view.Move(left,right,1d/hz); break;
                        case 1:
                            time-=1d/hz; Hands(time,0,direction,out left,out right); view.Move(left,right,1d/hz); break;
                        case 2:
                            for(int i=0;i<hz/4;i++)
                            { time+=1d/hz; Hands(time,0,direction,out left,out right); view.Move(left,right,1d/hz); }
                            break;
                        case 3: view.Input(true,1,1); view.Move(left,right,1d/hz); break;
                        case 4: view.Cancel(); break;
                        case 5: view.Input(false,0,0); break;
                        case 6: view.Move(left,right,.2); break;
                        case 7: view.Fit(20,MatrixD.Identity,Vector3D.Forward); break;
                        case 8: view.ChangeReference(MatrixD.Identity,MatrixD.CreateRotationY(.2)); break;
                        case 9: view.Rebase(Matrix.Identity,Matrix.CreateTranslation(0,.1f,0)); break;
                    }
                    double scale=view.UnitsPerMeter;
                    Hands(time+1d/hz,0,direction,out left,out right);
                    view.Input(true,0,0); view.Move(left,right,1d/hz); Finish(view,hz,left,right);
                    Require(view.UnitsPerMeter==scale,"Split release history survived a stop, expiry or cancellation: "+reason);
                }
                var jump=Drag(hz,0,direction,out var a,out var b);
                jump.Input(true,leftHeld ? 1:0,leftHeld ? 0:1);
                Hands(1+1d/hz,0,direction,out a,out b); jump.Move(a,b,1d/hz);
                if(leftHeld) b.Translation+=Vector3D.One; else a.Translation+=Vector3D.One;
                jump.Move(a,b,1d/hz);
                Require(!jump.Held && !jump.Coasting,"Released controller tracking jump survived pending zoom release");
            }
        }
        private static void RotationAndReversal(int hz)
        {
            var view=Drag(hz,0,0,out var a,out var b);
            var mid=(a.Translation+b.Translation)*.5;
            for(int i=1;i<=hz;i++)
            {
                var turn=MatrixD.CreateRotationY(i*.4/hz);
                a=b=turn;
                a.Translation=mid+Vector3D.TransformNormal(new Vector3D(-.2,0,0),turn);
                b.Translation=mid+Vector3D.TransformNormal(new Vector3D(.2,0,0),turn);
                view.Move(a,b,1d/hz);
            }
            var orientation=view.Orientation; var center=view.Center; double scale=view.UnitsPerMeter;
            view.Input(true,0,0); view.Move(a,b,1d/hz); Finish(view,hz,a,b);
            Near(view.Center,center,"Orbit's moving center was mistaken for pan velocity");
            Near(view.Orientation.Forward,orientation.Forward,"Release added angular momentum");
            Require(view.UnitsPerMeter==scale,"Pure orbit seeded zoom momentum");

            var reverse=Drag(hz,.3,.6,out a,out b);
            center=reverse.Center; scale=reverse.UnitsPerMeter;
            Hands(1-1d/hz,.3,.6,out a,out b);
            reverse.Input(true,0,0); reverse.Move(a,b,1d/hz); Finish(reverse,hz,a,b);
            Near(reverse.Center,center,"Release after reversal threw the view in its previous direction");
            Require(reverse.UnitsPerMeter==scale,"Release after reversal kept zooming in the old direction");
        }
        private static void Cancellations(int hz)
        {
            for(int reason=0;reason<7;reason++)
            {
                var view=Drag(hz,.3,.6,out var left,out var right);
                LetGo(view,hz,.3,.6,out left,out right);
                switch(reason)
                {
                    case 0: view.Input(false,0,0); break;
                    case 1: view.Input(true,0,0,true); break;
                    case 2: view.Cancel(); break;
                    case 3: view.ChangeReference(MatrixD.Identity,MatrixD.CreateRotationY(.4)); break;
                    case 4: view.Rebase(Matrix.Identity,Matrix.CreateTranslation(0,.1f,0)); break;
                    case 5: view.Fit(20,MatrixD.Identity,new Vector3D(0,0,-1)); break;
                    case 6: view.Move(left,right,.2); break;
                }
                var center=view.Center; double scale=view.UnitsPerMeter;
                Finish(view,hz,left,right);
                Near(view.Center,center,"Cancellation retained pan motion: "+reason);
                Require(view.UnitsPerMeter==scale,"Cancellation retained zoom motion: "+reason);
            }
            var release=Drag(hz,.3,.6,out var a,out var b);
            release.Input(true,0,0); a.Translation+=Vector3D.One;
            release.Move(a,b,1d/hz);
            Require(!release.Coasting && !release.Held,"Release tracking jump became a throw");
            Require(!release.Input(true,1,1),"Tracking cancellation reacquired held grips");

            var gate=new InputGate(); gate.Update(true,false); gate.Update(true,true); gate.Block();
            var flight=Drag(hz,.3,0,out a,out b); LetGo(flight,hz,.3,0,out a,out b);
            gate.Update(true,true); flight.Input(true,0,0,gate.Held);
            Require(!gate.Held && flight.Coasting,"A held flight input bypassed neutralization");
            gate.Update(true,false); gate.Update(true,true); flight.Input(true,0,0,gate.Held);
            Require(gate.Held && !flight.Coasting,"Fresh flight input did not brake the view");
        }
        private static void NoiseAndLimits(int hz)
        {
            var noise=Drag(hz,0,0,out var a,out var b);
            var center=noise.Center;
            for(int i=0;i<hz;i++)
            {
                Hands(0,0,0,out a,out b);
                a.Translation+=new Vector3D(i%2==0 ? .001:-.001,0,0);
                b.Translation+=new Vector3D(i%2==0 ? .001:-.001,0,0);
                noise.Move(a,b,1d/hz);
            }
            Require(Vector3D.Distance(noise.Center,center)<.001,"Stationary pan noise accumulated drift");
            noise.Input(true,0,0); noise.Move(a,b,1d/hz);
            Require(!noise.Coasting,"Stationary noise seeded a glide");

            foreach(double diameter in new[] {1d,1e9})
            {
                var limit=new Diorama(); limit.Fit(diameter,MatrixD.Identity,new Vector3D(0,0,-1));
                limit.Input(true,0,0); limit.Input(true,1,1);
                Hands(0,0,0,out a,out b); limit.Move(a,b,1d/hz);
                double direction=diameter>1 ? -1:1;
                for(int i=1;i<=hz*4;i++)
                {
                    Hands(i/(double)hz,0,direction,out a,out b); limit.Move(a,b,1d/hz);
                    Require(limit.UnitsPerMeter>=Diorama.MinScale && limit.UnitsPerMeter<=Diorama.MaxScale,"Smoothed zoom escaped scale limits");
                }
                limit.Input(true,0,0); limit.Move(a,b,1d/hz); Finish(limit,hz,a,b);
                Require(limit.UnitsPerMeter>=Diorama.MinScale && limit.UnitsPerMeter<=Diorama.MaxScale,"Zoom coast escaped scale limits");
            }
        }
    }
}
