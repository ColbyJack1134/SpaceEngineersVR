using System.Collections.Generic;
using SharpDX.Direct3D11;
using VRage.Game.Entity;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal static class TargetFeedback
    {
        internal sealed class View
        {
            internal readonly MatrixD Finger;
            internal readonly Vector3D Hit;
            internal readonly double Scale;
            internal readonly MatrixD? Grid;
            internal readonly BoundingBoxD Bounds;
            internal View(MatrixD finger,Vector3D hit,MyEntity entity,double scale)
            {
                Finger=finger; Hit=hit; Scale=scale;
                if(entity!=null) { Grid=entity.WorldMatrix; Bounds=entity.PositionComp.LocalAABB; }
            }
            internal View(MatrixD finger,Vector3D hit,double scale,MatrixD? grid,BoundingBoxD bounds)
            { Finger=finger; Hit=hit; Scale=scale; Grid=grid; Bounds=bounds; }
        }
        private static OverlayCanvas white;
        private static SharpDX.Direct3D11.Device device;
        private static ShaderResourceView texture;
        private static readonly List<NativeSprite> sprites=new List<NativeSprite>();
        internal static void Draw(Texture2D target,View state,MatrixD view,MatrixD projection,ShaderResourceView depth)
        {
            if(state==null) return;
            Prepare(target);
            sprites.Clear();
            var camera=MatrixD.Invert(view);
            var color=state.Grid.HasValue ? new Vector4(.3f,1,.65f,.9f):new Vector4(.24f,.8f,1,.75f);
            double size=.004*state.Scale;
            var glow=camera.GetOrientation(); glow.Translation=state.Finger.Translation;
            foreach(float layer in new[] {2.2f,1.5f,1f})
            {
                float radius=(float)(size*layer);
                var sprite=PhysicalSurface.Quad(texture,glow,new RectangleF(-radius,radius,2*radius,2*radius),new Vector4(0,0,1,1),
                    new Vector4(color.X,color.Y,color.Z,layer==1 ? .55f:.13f),view,projection);
                sprite.Rounded=new Vector2(.5f); sprite.IgnoreSceneDepth=true; sprites.Add(sprite);
            }
            AddLine(state.Finger.Translation,state.Hit,.0012*state.Scale,color,camera,view,projection);
            if(state.Grid.HasValue)
            {
                var corners=new Vector3D[8];
                unsafe { for(int i=0;i<8;i++) corners[i]=state.Bounds.GetCorner(i); }
                for(int i=0;i<8;i++) for(int j=i+1;j<8;j++)
                {
                    int differences=(corners[i].X!=corners[j].X ? 1:0)+(corners[i].Y!=corners[j].Y ? 1:0)+(corners[i].Z!=corners[j].Z ? 1:0);
                    if(differences==1) AddLine(Vector3D.Transform(corners[i],state.Grid.Value),Vector3D.Transform(corners[j],state.Grid.Value),
                        .0015*state.Scale,color,camera,view,projection);
                }
            }
            NativeSprites.Draw(target,sprites,depth);
        }
        private static void Prepare(Texture2D target)
        {
            if(device!=target.Device) Dispose();
            if(white==null)
            {
                device=target.Device; white=new OverlayCanvas("Selection feedback",8,8,1,false,target.Device);
                white.Graphics.Clear(System.Drawing.Color.White); white.Upload();
                texture=new ShaderResourceView(target.Device,white.Texture);
            }
        }
        private static void AddLine(Vector3D from,Vector3D to,double width,Vector4 color,MatrixD camera,MatrixD view,MatrixD projection)
        {
            var direction=to-from;
            double length=direction.Length();
            if(length<1e-6) return;
            direction/=length;
            var right=Vector3D.Cross(direction,camera.Translation-(from+to)*.5);
            if(right.LengthSquared()<1e-12) right=camera.Right;
            right.Normalize();
            var pose=MatrixD.CreateWorld(from,Vector3D.Cross(right,-direction),-direction);
            sprites.Add(PhysicalSurface.Quad(texture,pose,new RectangleF((float)(-width/2),0,(float)width,(float)length),
                new Vector4(0,0,1,1),color,view,projection));
        }
        internal static void Dispose() { texture?.Dispose(); texture=null; white?.Dispose(); white=null; device=null; sprites.Clear(); }
    }
}
