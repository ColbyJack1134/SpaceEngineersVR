#if SEVR_DIAGNOSTICS
using System;
using System.Collections.Generic;
using SpaceEngineersVR.Player;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class DlssTests
    {
        private static void Require(bool condition,string message) { if(!condition) throw new Exception("DLSS: "+message); }

        internal static void Run(Action<string> log)
        {
            Vector2 sum=Vector2.Zero,previous=Vector2.Zero;
            for(int i=0;i<16;i++)
            {
                var pixels=Dlss.Pixels(i);
                Require(Math.Abs(pixels.X)<=.5f && Math.Abs(pixels.Y)<=.5f,"jitter "+i+" leaves the pixel: "+pixels);
                Require(i==0 || pixels!=previous,"jitter "+i+" repeats the previous offset");
                previous=pixels; sum+=pixels;
            }
            Require(Math.Abs(sum.X/16)<.1f && Math.Abs(sum.Y/16)<.1f,"jitter sequence is biased: "+sum/16);
            var ndc=Dlss.Ndc(new Vector2(.5f,.5f),new Vector2I(2000,1000));
            Require(Math.Abs(ndc.X+.0005f)<1e-6f && Math.Abs(ndc.Y-.001f)<1e-6f,"NDC jitter scale: "+ndc);

            // A view point drawn with the jittered projection lands Shift pixels from where the plain projection puts it.
            var renderSize=new Vector2I(1648,1648);
            var jitter=Dlss.Ndc(new Vector2(.3f,-.2f),renderSize);
            MatrixD plain=VrMath.Projection(-1.1f,.9f,-1,1.05f,.03),jittered=plain;
            jittered.M31+=jitter.X; jittered.M32+=jitter.Y;
            var viewPoint=new Vector3D(.2,-.1,-.6);
            Vector2 Pixel(MatrixD m)
            {
                var clip=Vector4D.Transform(new Vector4D(viewPoint,1),m);
                return new Vector2((float)((clip.X/clip.W*.5+.5)*renderSize.X),(float)((.5-clip.Y/clip.W*.5)*renderSize.Y));
            }
            var moved=Pixel(jittered)-Pixel(plain)-Dlss.Shift(jitter,renderSize,plain.M34);
            Require(Math.Abs(moved.X)<1e-3f && Math.Abs(moved.Y)<1e-3f,"jitter shift does not match the projection: "+moved);

            var output=new Vector2I(2472,2472);
            Require(Dlss.Clamp(output,output,2f/3,false)==new Vector2I(1648,1648),"Quality render size");
            Require(Dlss.Clamp(new Vector2I(1730,1730),output,2f/3,true)==new Vector2I(1236,1236),"dynamic resolution went below the NGX minimum");
            Require(Dlss.Clamp(new Vector2I(1730,1730),output,1,true)==new Vector2I(1730,1730),"DLAA with dynamic resolution changed the size");
            Require(Dlss.Clamp(new Vector2I(1236,1236),output,1,false)==output,"DLAA accepted a half-size render");
            Require(Dlss.Clamp(new Vector2I(3000,3000),output,1,false)==output,"render exceeded the output");
            Require(Dlss.InRange(new Vector2I(1648,1648),output,2f/3,false),"Quality size refused without dynamic resolution");
            Require(Dlss.InRange(new Vector2I(1236,1236),output,.5f,false),"Performance size refused");
            Require(Dlss.InRange(output,output,1,false),"DLAA size refused");
            Require(!Dlss.InRange(new Vector2I(1236,1236),output,1,false),"DLAA accepted a half-size scene");
            Require(Dlss.InRange(new Vector2I(1236,1236),output,1,true),"DLAA with dynamic resolution refused a half-size scene");
            Require(!Dlss.InRange(new Vector2I(1153,1153),output,2f/3,true),"Quality accepted a scene below the NGX minimum");
            Require(!Dlss.InRange(output,output,2f/3,false),"Quality accepted the full-size scene of the previous mode");
            var cuts=new Dlss.Transitions();
            var size=new Vector2I(2000,2200);
            Require(cuts.Update(1,false,false,false,true,size,false),"first frame must reset");
            Require(!cuts.Update(1,false,false,false,true,size,false),"steady frame reset");
            Require(cuts.Update(2,false,false,false,true,size,false),"epoch change kept history");
            Require(cuts.Update(2,true,false,false,true,size,false),"third person kept history");
            Require(cuts.Update(2,true,true,false,true,size,false),"spectator kept history");
            Require(cuts.Update(2,true,true,true,true,size,false),"menu kept history");
            Require(cuts.Update(2,true,true,true,false,size,false),"lost rig kept history");
            Require(cuts.Update(2,true,true,true,false,new Vector2I(1800,1980),false),"resolution change kept history");
            Require(cuts.Update(2,true,true,true,false,new Vector2I(1800,1980),true),"recenter kept history");
            Require(!cuts.Update(2,true,true,true,false,new Vector2I(1800,1980),false),"reset flag stuck");
            Require(Dlss.Transitions.Jump(Vector3D.Zero,Vector3.Forward,new Vector3D(41,0,0),Vector3.Forward),"41 m jump kept history");
            Require(!Dlss.Transitions.Jump(Vector3D.Zero,Vector3.Forward,new Vector3D(39,0,0),Vector3.Forward),"39 m move reset history");
            Require(Dlss.Transitions.Jump(Vector3D.Zero,Vector3.Forward,Vector3D.Zero,Vector3.TransformNormal(Vector3.Forward,Matrix.CreateRotationY(MathHelper.ToRadians(40)))),"40 degree turn kept history");
            Require(!Dlss.Transitions.Jump(Vector3D.Zero,Vector3.Forward,Vector3D.Zero,Vector3.TransformNormal(Vector3.Forward,Matrix.CreateRotationY(MathHelper.ToRadians(30)))),"30 degree turn reset history");

            // A static point seen from two eye positions: the origin-shifted previous matrix must match a direct projection from the old eye.
            MatrixD projection=VrMath.Projection(-1.1f,.9f,-1,1,.05);
            Vector3D previousEye=new Vector3D(1000000,2000000,3000000),eye=previousEye+new Vector3D(1,.2,-.5);
            MatrixD orientation=MatrixD.CreateFromAxisAngle(Vector3D.Up,.3)*MatrixD.CreateFromAxisAngle(Vector3D.Right,-.1);
            MatrixD previousView=MatrixD.Invert(orientation*MatrixD.CreateTranslation(previousEye)),view=MatrixD.Invert(orientation*MatrixD.CreateTranslation(eye));
            MatrixD previousAt0=previousView; previousAt0.Translation=Vector3D.Zero;
            Matrix previousUnjittered=previousAt0*projection;
            Matrix shifted=Dlss.Previous(previousUnjittered,eye,previousEye);
            Vector3D point=previousEye+Vector3D.TransformNormal(new Vector3D(.4,-.3,-12),orientation);
            Vector4 viaShift=Vector4.Transform(new Vector4((Vector3)(point-eye),1),shifted);
            Vector4D direct=Vector4D.Transform(new Vector4D(point,1),previousView*projection);
            Require(Math.Abs(viaShift.X/viaShift.W-direct.X/direct.W)<1e-4 && Math.Abs(viaShift.Y/viaShift.W-direct.Y/direct.W)<1e-4,
                "previous reprojection drifted: "+(viaShift.X/viaShift.W)+","+(viaShift.Y/viaShift.W)+" vs "+(direct.X/direct.W)+","+(direct.Y/direct.W));
            Vector4D current=Vector4D.Transform(new Vector4D(point,1),view*projection);
            Require(Math.Abs(direct.X/direct.W-current.X/current.W)>.01,"eye translation produced no parallax");
            int[] bayer={0,32,8,40,2,34,10,42,48,16,56,24,50,18,58,26,12,44,4,36,14,46,6,38,60,28,52,20,62,30,54,22,
                3,35,11,43,1,33,9,41,51,19,59,27,49,17,57,25,15,47,7,39,13,45,5,37,63,31,55,23,61,29,53,21};
            for(int i=0;i<64;i++) Require(Dlss.Bayer(i%8,i/8)==bayer[i],"Bayer texel "+i+" differs from the engine's dither texture");
#if !LINUX
            Require(Dlss.MissingEngineMember==null,"engine member missing: "+Dlss.MissingEngineMember);
            Require(Dlss.MissingObjectMember==null,"object motion member missing: "+Dlss.MissingObjectMember);
            Objects();
#endif
            log("PASS DLSS: Halton jitter inside the pixel, NDC scale, jitter shift, NGX render range, history resets on epoch, view, menu, rig, size, recenter and jumps, previous-origin reprojection, engine dither pattern, engine state and depth members, object reprojection, tracking and box order");
        }

#if !LINUX
        private static void Objects()
        {
            // A point fixed on a grid that flew and turned far from the origin: its camera-relative position must reproject to where it was.
            Vector3D camera=new Vector3D(1000000,2000000,3000000);
            MatrixD previous=MatrixD.CreateFromAxisAngle(Vector3D.Up,.4)*MatrixD.CreateTranslation(camera+new Vector3D(3,-2,-20));
            MatrixD current=MatrixD.CreateFromAxisAngle(Vector3D.Up,.43)*MatrixD.CreateFromAxisAngle(Vector3D.Right,.02)*MatrixD.CreateTranslation(camera+new Vector3D(3.5,-1.8,-21.5));
            Vector3D local=new Vector3D(4,1.5,-7);
            var now=(Vector3)(Vector3D.Transform(local,current)-camera);
            Vector3D before=Vector3D.Transform(local,previous)-camera;
            var reprojected=Vector3.Transform(now,DlssObjects.Reproject(current,previous,camera));
            Require(Vector3D.Distance(reprojected,before)<1e-3,"object reprojection is off by "+Vector3D.Distance(reprojected,before)+" m");
            Require(Vector3D.Distance(Vector3.Transform(now,DlssObjects.ToLocal(current,camera)),local)<1e-3,"box space does not match the object");
            Require(Vector3D.Distance(now,before)>.5,"object did not move in the fixture");

            var tracker=new DlssObjects.Tracker();
            MatrixD seen; bool moved;
            Require(!tracker.Observe(7,previous,10,out seen,out moved),"first sighting had a previous matrix");
            Require(tracker.Observe(7,previous,11,out seen,out moved) && !moved,"resting actor reported as moving");
            Require(tracker.Observe(7,current,12,out seen,out moved) && moved && DlssObjects.Same(seen,previous),"moving actor lost its previous matrix");
            Require(tracker.Observe(7,previous,12,out seen,out moved) && moved && DlssObjects.Same(seen,previous),"second eye changed the frame's matrices");
            Require(!tracker.Observe(7,current,14,out seen,out moved),"matrix from two frames ago used as previous");
            Require(tracker.Observe(7,current,15,out seen,out moved) && !moved,"tracking did not resume");
            MatrixD jumped=current; jumped.Translation+=new Vector3D(0,0,2000);
            Require(!tracker.Observe(7,jumped,16,out seen,out moved),"a 2 km jump counted as motion");
            tracker.Prune(40);
            Require(tracker.Count==0,"stale tracks kept");

            // Two cells of one grid merge into one box; a small grid inside it keeps its own motion and draws last.
            var cell=new BoundingBox(new Vector3(-10),new Vector3(10));
            var inner=new BoundingBox(new Vector3(-2),new Vector3(2));
            MatrixD innerNow=MatrixD.CreateRotationY(.1)*current,innerBefore=previous;
            var list=new List<DlssObjects.Candidate>
            {
                new DlssObjects.Candidate { Current=innerNow,Previous=innerBefore,Bounds=inner,Distance=5 },
                new DlssObjects.Candidate { Current=current,Previous=previous,Bounds=cell,Distance=9 },
                new DlssObjects.Candidate { Current=current,Previous=previous,Bounds=new BoundingBox(new Vector3(-10,-10,10),new Vector3(10,10,25)),Distance=20 }
            };
            DlssObjects.Emit(list,camera,Matrix.Identity,Matrix.Identity);
            Require(DlssObjects.BoxCount==2,"expected two grids, got "+DlssObjects.BoxCount+" boxes");
            Require(DlssObjects.Boxes[0].Min.Z==-10 && DlssObjects.Boxes[0].Max.Z==25,"cells of one grid did not merge into one box");
            Require(DlssObjects.Boxes[1].Max.X==2 && DlssObjects.Boxes[1].Index!=DlssObjects.Boxes[0].Index,"the inner grid must draw last with its own motion");
            var innerPoint=(Vector3)(Vector3D.Transform(new Vector3D(1,0,1),innerNow)-camera);
            Require(Vector3D.Distance(Vector3.Transform(innerPoint,DlssObjects.Transforms[DlssObjects.Boxes[1].Index]),Vector3D.Transform(new Vector3D(1,0,1),innerBefore)-camera)<1e-3,"box index does not select its own motion");
            DlssObjects.Clear();
        }
#endif
    }
}
#endif
