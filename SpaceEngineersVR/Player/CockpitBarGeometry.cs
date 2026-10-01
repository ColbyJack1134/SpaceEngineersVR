using VRageMath;
using VRageRender.Messages;

namespace SpaceEngineersVR.Player
{
    internal static class CockpitBarGeometry
    {
        internal const int Slot=CockpitSwitchGeometry.Count;
        internal const float Travel=.030f;
        internal static readonly Vector3 Center=new Vector3(.326782227f,-.378417972f,-.037055970f);
        internal static readonly Vector3 Front=new Vector3(.320800781f,-.365539554f,-.034682274f);
        internal static readonly Vector3 Normal=new Vector3(-.458578851f,.860696285f,.221150045f);
        private static readonly Vector3 up=new Vector3(.63224629f,.4908655f,-.59942946f);
        internal static MatrixD TouchPose => MatrixD.CreateWorld(Front,-Normal,up);
        internal static Matrix Visual(float position) => Matrix.CreateTranslation(Normal*(Travel*MathHelper.Clamp(position,0,1)));
        internal static void ExtendStem(MyModelData mesh)
        {
            // Extra stem slides inside the opaque housing; no per-frame mesh uploads or new actor.
            var moved=new bool[mesh.Positions.Count];
            mesh.AABB=BoundingBox.CreateInvalid();
            for(int i=0;i<mesh.Positions.Count;i++)
            {
                moved[i]=Vector3.Dot(mesh.Positions[i],Normal)<-.494f;
                if(moved[i]) mesh.Positions[i]-=Normal*Travel;
                mesh.AABB.Include(mesh.Positions[i]);
            }
            for(int i=0;i<mesh.Indices.Count;i+=3)
            {
                int a=mesh.Indices[i],b=mesh.Indices[i+1],c=mesh.Indices[i+2];
                if(!moved[a] && !moved[b] && !moved[c]) continue;
                Vector3 ab=mesh.Positions[b]-mesh.Positions[a],ac=mesh.Positions[c]-mesh.Positions[a];
                Vector3 normal=Vector3.Normalize(Vector3.Cross(ab,ac));
                if(Vector3.Dot(normal,mesh.Normals[a])<0) normal=-normal;
                Vector2 du=mesh.TexCoords[b]-mesh.TexCoords[a],dv=mesh.TexCoords[c]-mesh.TexCoords[a];
                float determinant=du.X*dv.Y-du.Y*dv.X;
                Vector3 tangent=System.Math.Abs(determinant)>1e-8f ? (ab*dv.Y-ac*du.Y)/determinant : mesh.Tangents[a];
                tangent=Vector3.Normalize(tangent-normal*Vector3.Dot(normal,tangent));
                for(int j=0;j<3;j++) { int v=mesh.Indices[i+j]; mesh.Normals[v]=normal; mesh.Tangents[v]=tangent; }
            }
        }
    }
}
