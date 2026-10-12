using System;
using System.Collections.Generic;
using HarmonyLib;
using Sandbox.Game.Gui;
using Sandbox.Game.World;
using SharpDX.Direct3D11;
using SpaceEngineersVR.Plugin;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal static class DamageMarkers
    {
        private sealed class Marker { public Vector3D Origin,Up; public DateTime Time; public Vector2? Direction; }
        private const string Texture=@"Textures\GUI\Indicators\DamageIndicator03.png";
        // Vanilla draws the 22x38 right edge of the 128 px texture.
        private static readonly Vector4 Source=new Vector4(106/128f,45/128f,22/128f,38/128f);
        private static readonly TimeSpan Lifetime=TimeSpan.FromSeconds(1.5);
        private const double Scale=1.5,Depth=1.5;
        // Vanilla ring radius is a sixth of the screen height; markers are 38 px of 1080.
        private static readonly double Radius=MarkerBillboard.DesktopHalfField/3,Height=38/540.0*MarkerBillboard.DesktopHalfField*Scale,Width=Height*22/38;
        private static readonly List<Marker> markers=new List<Marker>();

        internal static void Add(Vector3D origin)
        {
            var character=MySession.Static?.LocalCharacter;
            if(Main.VrActive && character!=null) Add(origin,character.WorldMatrix.Up,DateTime.UtcNow);
        }
        internal static void Add(Vector3D origin,Vector3D up,DateTime time)
        {
            lock(markers) markers.Add(new Marker {Origin=origin,Up=up,Time=time});
        }
        internal static void Reset() { lock(markers) markers.Clear(); }

        // Vanilla EaseInBack fade with 0.8 peak alpha.
        private static float Alpha(double seconds)
        {
            double x=seconds/Lifetime.TotalSeconds,s=1.7015800476074219;
            return (float)MathHelper.Clamp((1-((s+1)*x*x*x-s*x*x))*.8,0,1);
        }

        // Vanilla projects the shooter onto the character's horizontal plane: x right, y toward behind.
        private static Vector2? Direction(MatrixD head,Vector3D up,Vector3D origin)
        {
            var level=VrMath.Level(head,up);
            var local=origin-level.Translation;
            var direction=new Vector2((float)Vector3D.Dot(local,level.Right),(float)Vector3D.Dot(local,level.Backward));
            return direction.LengthSquared()>1e-8f ? Vector2.Normalize(direction):(Vector2?)null;
        }

        internal static void Draw(Texture2D target,MatrixD head,MatrixD view,MatrixD projection)
        {
            Marker[] current;
            var now=DateTime.UtcNow;
            lock(markers)
            {
                markers.RemoveAll(m=>now-m.Time>Lifetime);
                if(markers.Count==0) return;
                current=markers.ToArray();
            }
            var sprites=new List<NativeSprite>(current.Length);
            foreach(var marker in current)
            {
                // Fixed when first shown, as vanilla fixes the screen point at the hit.
                if(marker.Direction==null) marker.Direction=Direction(head,marker.Up,marker.Origin);
                if(marker.Direction==null) continue;
                var d=marker.Direction.Value;
                var outward=head.Right*d.X-head.Up*d.Y;
                var position=head.Translation+(head.Forward+outward*Radius)*Depth;
                // Billboard right is the outward ring direction, matching the texture's outer edge.
                if(!MarkerBillboard.TryCreate(position,head,Vector3D.Cross(outward,head.Forward),false,out var board)) continue;
                var sprite=new NativeSprite(Texture,default(RectangleF),new Vector4(1,0,0,Alpha((now-marker.Time).TotalSeconds)))
                    {UV=Source,NativeGui=true,Premultiplied=true,IgnoreSceneDepth=true};
                double w=Width*Depth/board.Scale,h=Height*Depth/board.Scale;
                if(board.Project(new RectangleF((float)(-w/2),(float)(-h/2),(float)w,(float)h),view,projection,ref sprite)) sprites.Add(sprite);
            }
            if(sprites.Count>0) NativeSprites.Draw(target,sprites.ToArray());
        }
    }

    [HarmonyPatch(typeof(MyGuiScreenHudSpace),nameof(MyGuiScreenHudSpace.AddDamageIndicator))]
    internal static class DamageIndicatorPatch
    {
        private static void Postfix(Vector3D origin) => DamageMarkers.Add(origin);
    }
}
