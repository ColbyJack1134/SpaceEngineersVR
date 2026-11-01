using System;
using System.Collections.Generic;
using VRageMath;
using VRageRender.Import;
using VRageRender.Messages;

namespace SpaceEngineersVR.Player
{
    // Raised key caps and slider knobs built from a native flat face, textured from the same atlas region.
    internal static class CockpitCaps
    {
        // ButtonsAtlas key cap drawn unlit; the lit drawing of the same cap sits 203 texels to the right (2048 atlas).
        private static readonly RectangleF unlitCap=new RectangleF(1088f/2048,681f/2048,120f/2048,56f/2048);
        private const float LitShift=203f/2048,LitStart=1250f/2048,CapCorner=7f/2048,WallTexel=2f/2048;
        private const int RoundSides=24,CornerSides=5;
        // Walls reach below the face so no gap shows while pressed; surfaces sit just above native art, patches above them.
        private const float WallDepth=.001f,SurfaceLift=.0003f,PatchLift=.0008f;
        // Walls sample texels this far inside the cap outline, avoiding the atlas cutout border.
        private const float WallSample=.12f;

        // Native button art is a decal that can paint over geometry just in front of it; copies are alpha-tested solids.
        internal const string SolidSuffix="#solid";
        internal static string NativeMaterial(string material) => material.EndsWith(SolidSuffix,StringComparison.Ordinal) ? material.Substring(0,material.Length-SolidSuffix.Length):material;
        internal static MyMaterialDescriptor Solid(MyMaterialDescriptor source)
        {
            var solid=new MyMaterialDescriptor(source.MaterialName+SolidSuffix) { Technique="ALPHA_MASKED" };
            foreach(var texture in source.Textures) solid.Textures[texture.Key]=texture.Value;
            foreach(var data in source.UserData) solid.UserData[data.Key]=data.Value;
            return solid;
        }

        internal static void Build(MyModelData target,CockpitRig.Face face,bool showLit)
        {
            if(face.Lit) { BuildKey(target,face,showLit); return; }
            var uv=face.Uv;
            Vector3 n=Vector3.Normalize(face.Normal);
            Vector3 Point(Vector2 st) => Vector3.Lerp(Vector3.Lerp(face.Corners[0],face.Corners[1],st.X),Vector3.Lerp(face.Corners[3],face.Corners[2],st.X),st.Y);
            Vector2 Tex(Vector2 st) => Vector2.Lerp(Vector2.Lerp(uv[0],uv[1],st.X),Vector2.Lerp(uv[3],uv[2],st.X),st.Y);
            var outline=new List<Vector2>();
            if(face.Round)
                for(int i=0;i<RoundSides;i++)
                {
                    double a=2*Math.PI*i/RoundSides; float r=.5f-face.Inset.X;
                    outline.Add(new Vector2(.5f+r*(float)Math.Cos(a),.5f+r*(float)Math.Sin(a)));
                }
            else
            {
                outline.Add(new Vector2(face.Inset.X,face.Inset.Y)); outline.Add(new Vector2(1-face.Inset.X,face.Inset.Y));
                outline.Add(new Vector2(1-face.Inset.X,1-face.Inset.Y)); outline.Add(new Vector2(face.Inset.X,1-face.Inset.Y));
            }
            var center=new Vector2(.5f);
            Extrude(target,face,outline.ConvertAll(Point),outline.ConvertAll(Tex),outline.ConvertAll(st=>Tex(Vector2.Lerp(st,center,WallSample))),Point(center),Tex(center),Tangent(face.Corners,uv,n));
        }

        internal static void Build(MyModelData target,CockpitRig.Surface surface)
        {
            Vector3 n=Vector3.Normalize(surface.Normal);
            var p=surface.Positions; var uv=new Vector2[surface.Uv.Length];
            for(int i=0;i<uv.Length;i++) uv[i]=surface.Unlit ? Retile(surface.Uv[i],false):surface.Uv[i];
            var mesh=new Mesh(target,Tangent(new[] {p[0],p[1],p[0]+(p[2]-p[1]),p[2]},new[] {uv[0],uv[1],uv[0]+(uv[2]-uv[1]),uv[2]},n));
            for(int i=0;i+2<p.Length;i+=3) mesh.Triangle(p[i]+n*SurfaceLift,p[i+1]+n*SurfaceLift,p[i+2]+n*SurfaceLift,uv[i],uv[i+1],uv[i+2],n);
            mesh.Finish(surface.Material+SolidSuffix);
        }

        // The cap follows the drawn key's rounded outline so both atlas drawings land on the same geometry.
        private static void BuildKey(MyModelData target,CockpitRig.Face face,bool showLit)
        {
            var uv=new Vector2[4];
            for(int i=0;i<4;i++) uv[i]=Retile(face.Uv[i],false);
            Vector2 d1=uv[1]-uv[0],d2=uv[3]-uv[0]; float det=d1.X*d2.Y-d1.Y*d2.X;
            Vector3 Point(Vector2 t)
            {
                Vector2 r=t-uv[0]; float a=(r.X*d2.Y-r.Y*d2.X)/det,b=(d1.X*r.Y-d1.Y*r.X)/det;
                return face.Corners[0]+(face.Corners[1]-face.Corners[0])*a+(face.Corners[3]-face.Corners[0])*b;
            }
            Vector2 Tex(Vector2 t) => showLit ? new Vector2(t.X+LitShift,t.Y):t;
            var outline=new List<Vector2>();
            float l=unlitCap.X,top=unlitCap.Y,r2=unlitCap.X+unlitCap.Width,bottom=unlitCap.Y+unlitCap.Height;
            var corners=new[] {new Vector2(r2-CapCorner,bottom-CapCorner),new Vector2(l+CapCorner,bottom-CapCorner),new Vector2(l+CapCorner,top+CapCorner),new Vector2(r2-CapCorner,top+CapCorner)};
            for(int c=0;c<4;c++) for(int i=0;i<=CornerSides;i++)
            {
                double a=Math.PI/2*(c+(double)i/CornerSides);
                outline.Add(corners[c]+new Vector2((float)Math.Cos(a),(float)Math.Sin(a))*CapCorner);
            }
            Vector2 Inside(Vector2 t) => new Vector2(MathHelper.Clamp(t.X,l+WallTexel,r2-WallTexel),MathHelper.Clamp(t.Y,top+WallTexel,bottom-WallTexel));
            var center=new Vector2(l+unlitCap.Width/2,top+unlitCap.Height/2);
            Extrude(target,face,outline.ConvertAll(Point),outline.ConvertAll(Tex),outline.ConvertAll(t=>Tex(Inside(t))),Point(center),Tex(center),
                Tangent(face.Corners,uv,Vector3.Normalize(face.Normal)));
        }

        private static void Extrude(MyModelData target,CockpitRig.Face face,List<Vector3> points,List<Vector2> tex,List<Vector2> wallTex,Vector3 center,Vector2 centerTex,Vector3 tangent)
        {
            Vector3 n=Vector3.Normalize(face.Normal);
            var mesh=new Mesh(target,tangent);
            float height=face.Height>0 ? face.Height:PatchLift;
            for(int i=0;i<points.Count;i++)
            {
                int j=(i+1)%points.Count;
                mesh.Triangle(center+n*height,points[i]+n*height,points[j]+n*height,centerTex,tex[i],tex[j],n);
            }
            if(face.Height>0)
                for(int i=0;i<points.Count;i++)
                {
                    int j=(i+1)%points.Count; Vector3 pa=points[i],pb=points[j];
                    if(Vector3.DistanceSquared(pa,pb)<1e-12f) continue;
                    Vector3 side=Vector3.Cross(pb-pa,n); side.Normalize();
                    if(Vector3.Dot(side,(pa+pb)*.5f-center)<0) side=-side;
                    Vector3 low=-n*WallDepth,high=n*height,along=Vector3.Normalize(pb-pa);
                    mesh.Triangle(pa+low,pb+low,pb+high,wallTex[i],wallTex[j],wallTex[j],side,along);
                    mesh.Triangle(pa+low,pb+high,pa+high,wallTex[i],wallTex[j],wallTex[i],side,along);
                }
            mesh.Finish(face.Material+SolidSuffix);
        }

        internal static Vector2 Retile(Vector2 uv,bool toLit)
        {
            bool isLit=uv.X>=LitStart;
            return isLit==toLit ? uv : new Vector2(uv.X+(toLit ? LitShift:-LitShift),uv.Y);
        }

        private static Vector3 Tangent(Vector3[] p,Vector2[] uv,Vector3 n)
        {
            Vector3 e1=p[1]-p[0],e2=p[3]-p[0]; Vector2 d1=uv[1]-uv[0],d2=uv[3]-uv[0];
            float det=d1.X*d2.Y-d1.Y*d2.X;
            Vector3 t=Math.Abs(det)>1e-12f ? (e1*d2.Y-e2*d1.Y)/det:e1;
            t-=n*Vector3.Dot(n,t);
            return Vector3.Normalize(t);
        }

        private sealed class Mesh
        {
            private readonly MyModelData target;
            private readonly Vector3 tangent;
            private readonly int firstIndex;
            public Mesh(MyModelData target,Vector3 tangent) { this.target=target; this.tangent=tangent; firstIndex=target.Indices.Count; }
            public void Triangle(Vector3 a,Vector3 b,Vector3 c,Vector2 ta,Vector2 tb,Vector2 tc,Vector3 n,Vector3? t=null)
            {
                // Installed cockpit meshes wind front faces clockwise around their normal.
                if(Vector3.Dot(Vector3.Cross(b-a,c-a),n)>0) { var swap=b; b=c; c=swap; var tswap=tb; tb=tc; tc=tswap; }
                foreach(var (p,uv) in new[] {(a,ta),(b,tb),(c,tc)})
                {
                    target.Indices.Add(target.Positions.Count); target.Positions.Add(p); target.AABB.Include(p);
                    target.Normals.Add(n); target.Tangents.Add(t ?? tangent); target.TexCoords.Add(uv);
                }
            }
            public void Finish(string material) =>
                target.Sections.Add(new MyRuntimeSectionInfo { IndexStart=firstIndex,TriCount=(target.Indices.Count-firstIndex)/3,MaterialName=material });
        }
    }
}
