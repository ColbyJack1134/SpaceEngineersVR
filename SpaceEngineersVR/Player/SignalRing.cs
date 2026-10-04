using System;
using System.Collections.Generic;
using HarmonyLib;
using Sandbox.Game.GUI;
using Sandbox.Graphics.GUI;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal sealed class SignalRing
    {
        public string Texture,FilledTexture;
        public Vector2 Size,SegmentSize,Origin;
        public Vector4 FocusColor,LockColor;
        public int Segments;
        public float Angle,Offset,Progress;
        public bool ShowEmpty;
        internal static SignalRing Read(MyStatControlTargetingProgressBar circle)
        {
            var texture=(MyGuiSizedTexture)AccessTools.Field(typeof(MyStatControlCircularProgressBar),"m_texture").GetValue(circle);
            var filled=(MyGuiSizedTexture?)AccessTools.Field(typeof(MyStatControlTargetingProgressBar),"m_filledTexture").GetValue(circle);
            bool enemy=circle.TargetType==MyStatControlTargetingProgressBar.ProgressBarTargetType.Enemy;
            bool friendly=circle.TargetType==MyStatControlTargetingProgressBar.ProgressBarTargetType.Friendly;
            return new SignalRing { Texture=texture.Texture,FilledTexture=filled?.Texture ?? texture.Texture,Size=circle.Size,
                SegmentSize=circle.SegmentSize,Origin=circle.SegmentOrigin,Segments=circle.NumberOfSegments,Angle=circle.TextureRotationAngle,
                Offset=circle.TextureRotationOffset,Progress=circle.StatMaxValue>0 ? MathHelper.Clamp(circle.StatCurrent/circle.StatMaxValue,0,1):0,ShowEmpty=circle.ShowEmptySegments,
                FocusColor=enemy ? circle.EnemyFocusSegmentColorMask:friendly ? circle.FriendlyFocusSegmentColorMask:circle.NeutralFocusSegmentColorMask,
                LockColor=enemy ? circle.EnemyLockingSegmentColorMask:friendly ? circle.FriendlyLockingSegmentColorMask:circle.NeutralLockingSegmentColorMask };
        }
        internal void Add(List<NativeSprite> sprites,MarkerBillboard board,MatrixD view,MatrixD projection)
        {
            if(Size.X<=0 || Segments<=0) return;
            for(int i=0;i<Segments;i++)
            {
                double angle=MathHelper.ToRadians(Angle*i+Offset);
                var rotated=board;
                rotated.Right=board.Right*Math.Cos(angle)-board.Up*Math.Sin(angle);
                rotated.Up=board.Right*Math.Sin(angle)+board.Up*Math.Cos(angle);
                // Native segment rectangles rotate about the centre of the whole control.
                float unit=4.5f/Size.X;
                var bounds=new RectangleF((-Origin.X-Size.X/2)*unit,(Origin.Y-Size.Y/2)*unit,SegmentSize.X*unit,SegmentSize.Y*unit);
                if(ShowEmpty) AddSegment(sprites,rotated,view,projection,Texture,bounds,new Vector4(0,0,1,1),FocusColor);
                float fill=MathHelper.Clamp(Progress*Segments-i,0,1);
                if(fill<=0) continue;
                bounds.Y+=bounds.Height*(1-fill); bounds.Height*=fill;
                AddSegment(sprites,rotated,view,projection,FilledTexture,bounds,new Vector4(0,1-fill,1,fill),LockColor);
            }
        }
        internal void AddNative(List<NativeSprite> sprites,Vector3D position,MatrixD view,MatrixD projection,int viewportWidth,Vector3D? up=null,bool faceViewer=false)
        {
            if(MarkerBillboard.TryCreatePixels(position,Size.X/4.5,view,projection,viewportWidth,out var board,up,faceViewer))
                Add(sprites,board,view,projection);
        }
        private static void AddSegment(List<NativeSprite> sprites,MarkerBillboard board,MatrixD view,MatrixD projection,string texture,RectangleF bounds,Vector4 uv,Vector4 color)
        {
            var sprite=new NativeSprite(texture,default(RectangleF),color.ToLinearRGB()) {UV=uv,Premultiplied=true};
            if(board.Project(bounds,view,projection,ref sprite)) sprites.Add(sprite);
        }
    }
}
