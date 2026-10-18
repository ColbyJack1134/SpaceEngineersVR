using System;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.Weapons;
using Sandbox.Game.World;
using SharpDX.Direct3D11;
using SpaceEngineersVR.Plugin;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    // The native hit and low health overlay (MyGuiControlBloodOverlay) is a desktop GUI sprite the eyes never get.
    internal static class BloodOverlay
    {
        private const string Texture=@"Textures\GUI\Blood.dds";
        private const float Inset=.85f;
        private static readonly RenderRecovery recovery=new RenderRecovery("VR blood overlay");

        // Vanilla shows it whether or not the HUD is hidden, only while the local character is in control or piloting.
        internal static float Alpha()
        {
            var session=MySession.Static;
            var character=session?.LocalCharacter;
            if(!(character?.Render is Sandbox.Game.Components.MyRenderComponentCharacter render)) return 0;
            var controlled=session.ControlledEntity;
            if(controlled!=character && !(controlled is MyCockpit cockpit && cockpit.Pilot==character) && !(controlled is MyLargeTurretBase turret && turret.Pilot==character)) return 0;
            return render.GetHUDBloodAlpha();
        }

        internal static void Draw(Texture2D target,MatrixD projection)
        {
            if(recovery.Failed || !Main.WorldAvailable) return;
            try { Draw(target,projection,Alpha()); }
            catch(Exception ex) { recovery.Fail(ex,"VR blood overlay disabled"); }
        }
        internal static void Draw(Texture2D target,MatrixD projection,float alpha)
        {
            if(!(alpha>0)) return;
            // Head-locked at infinity, symmetric about the view axis; the inset copy brings the vignette edge inside the lens view.
            double x=(Math.Abs(projection.M31)+1)/projection.M11,y=(Math.Abs(projection.M32)+1)/projection.M22;
            NativeSprites.Draw(target,new[] {Sprite(x,y,alpha*.6f,projection),Sprite(x*Inset,y*Inset,alpha,projection)});
        }
        private static NativeSprite Sprite(double x,double y,float alpha,MatrixD projection) =>
            new NativeSprite(Texture,default(RectangleF),new Vector4(1,1,1,Math.Min(alpha,1)))
                {NativeGui=true,Premultiplied=true,IgnoreSceneDepth=true,Projected=true,
                 TopLeft=Corner(-x,y,projection),TopRight=Corner(x,y,projection),BottomLeft=Corner(-x,-y,projection),BottomRight=Corner(x,-y,projection)};
        private static Vector4 Corner(double x,double y,MatrixD projection)
        {
            var clip=Vector4D.Transform(new Vector4D(x,y,-1,0),projection);
            return new Vector4((float)clip.X,(float)clip.Y,0,(float)clip.W);
        }
    }
}
