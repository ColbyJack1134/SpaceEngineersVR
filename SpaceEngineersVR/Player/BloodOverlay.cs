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
            // Head-locked at infinity and symmetric about the view axis, so both eyes see the same frame over their whole field.
            double x=(Math.Abs(projection.M31)+1)/projection.M11,y=(Math.Abs(projection.M32)+1)/projection.M22;
            var sprite=new NativeSprite(Texture,default(RectangleF),new Vector4(1,1,1,Math.Min(alpha,1)))
                {NativeGui=true,Premultiplied=true,IgnoreSceneDepth=true,Projected=true,
                 TopLeft=Corner(-x,y,projection),TopRight=Corner(x,y,projection),BottomLeft=Corner(-x,-y,projection),BottomRight=Corner(x,-y,projection)};
            NativeSprites.Draw(target,new[] {sprite});
        }
        private static Vector4 Corner(double x,double y,MatrixD projection)
        {
            var clip=Vector4D.Transform(new Vector4D(x,y,-1,0),projection);
            return new Vector4((float)clip.X,(float)clip.Y,0,(float)clip.W);
        }
    }
}
