using System;
using System.Collections.Generic;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal sealed class SeatPanelMount
    {
        internal readonly MatrixD Pose;
        internal readonly float Width,Height;
        internal readonly Vector3D[] Faces;
        private static readonly Dictionary<string,SeatPanelMount> mounts=Create();
        internal static SeatPanelMount Find(string subtype) => mounts.TryGetValue(subtype,out var mount) ? mount:null;
        private SeatPanelMount(MatrixD pose,float width,float height,List<Vector3D> faces)
        {
            Pose=pose; Width=width; Height=height;
            var inverse=MatrixD.Invert(pose);
            Faces=faces.ConvertAll(p=>Vector3D.Transform(p,inverse)).ToArray();
        }
        private static MatrixD Frame(Vector3D center,Vector3D normal) => MatrixD.CreateWorld(center,-normal,Vector3D.Normalize(Vector3D.Cross(normal,Vector3D.Right)));
        private static Dictionary<string,SeatPanelMount> Create()
        {
            var result=new Dictionary<string,SeatPanelMount>();
            foreach(string subtype in new[] {"SmallBlockCapCockpit","LargeBlockCockpitSeat","OpenCockpitSmall"})
            {
                bool cab=subtype=="SmallBlockCapCockpit",open=subtype=="OpenCockpitSmall";
                var center=open ? new Vector3D(0,-.02,.16) : cab ? new Vector3D(0,-.25,-.045) : new Vector3D(0,-.49,-.185);
                double angle=(open ? 20:30)*Math.PI/180;
                var normal=new Vector3D(0,Math.Sin(angle),Math.Cos(angle));
                var pose=Frame(center,normal);
                float width=open ? .072f:.108f,height=open ? .080f:.120f;
                var anchor=open ? new Vector3D(0,.03202634,.12576281) : cab ? new Vector3D(0,-.25,-.07836914) : new Vector3D(0,-.59,-.24939);
                var anchorNormal=open ? Vector3D.Normalize(new Vector3D(0,.94013576,.34080017)):Vector3D.Backward;
                var faces=new List<Vector3D>();
                if(cab)
                {
                    var vertices=new Vector3D[8];
                    for(int i=0;i<4;i++)
                    {
                        var front=Vector3D.Transform(new Vector3D(i==0 || i==3 ? -.056:.056,i<2 ? -.062:.062,-.001),pose);
                        vertices[i+4]=front;
                        vertices[i]=front-anchorNormal*Vector3D.Dot(front-anchor,anchorNormal);
                    }
                    AddFaces(faces,vertices,new[] {0,3,2,1,4,5,6,7,0,1,5,4,1,2,6,5,2,3,7,6,3,0,4,7});
                }
                else
                {
                    Box(faces,Frame(center-normal*(open ? .007:.010),normal),new Vector3D(width+.004,height+.004,open ? .012:.018));
                    Box(faces,Frame(anchor+anchorNormal*(open ? .003:.005),anchorNormal),open ? new Vector3D(.030,.014,.006):new Vector3D(.040,.040,.010));
                    var start=anchor+anchorNormal*(open ? .001:.010);
                    var end=center-normal*(open ? .020:.019);
                    var z=Vector3D.Normalize(end-start);
                    var x=Vector3D.Normalize(Vector3D.Cross(Vector3D.Up,z));
                    var stem=MatrixD.Identity; stem.Right=x; stem.Up=Vector3D.Cross(z,x); stem.Backward=z; stem.Translation=(start+end)/2;
                    Box(faces,stem,new Vector3D(open ? .012:.024,open ? .012:.024,Vector3D.Distance(start,end)));
                }
                result.Add(subtype,new SeatPanelMount(pose,width,height,faces));
            }
            var flushNormal=Vector3D.Normalize(new Vector3D(0,.7456467816208012,.6663414117841854));
            result.Add("SmallBlockFlushCockpit",new SeatPanelMount(Frame(new Vector3D(0,-.2634839533551725,-.06462935425186113),flushNormal),.108f,.120f,new List<Vector3D>()));
            return result;
        }
        private static void Box(List<Vector3D> faces,MatrixD pose,Vector3D size)
        {
            var vertices=new Vector3D[8];
            for(int i=0;i<8;i++) vertices[i]=Vector3D.Transform(new Vector3D((i%2==0 ? -.5:.5)*size.X,(i%4<2 ? -.5:.5)*size.Y,(i<4 ? -.5:.5)*size.Z),pose);
            AddFaces(faces,vertices,new[] {0,1,3,2,4,6,7,5,0,4,5,1,2,3,7,6,0,2,6,4,1,5,7,3});
        }
        private static void AddFaces(List<Vector3D> faces,Vector3D[] vertices,int[] indices)
        {
            foreach(int i in indices) faces.Add(vertices[i]);
        }
    }
}
