using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Engine.Networking;
using Sandbox.Game.Multiplayer;
using Sandbox.Game.VoiceChat;
using Sandbox.Game.World;
using SharpDX.Direct3D11;
using VRage.GameServices;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    // Vanilla draws its voice icon above talking players on the desktop screen only.
    internal static class VoiceMarkers
    {
        // Vanilla's TEXTURE_VOICE_CHAT; the HUD draws it 128 px of the 1200 px GUI height.
        internal const string Icon=@"Textures\GUI\Icons\VoiceIcon.dds";
        internal const float IconScreenHeight=128/1200f;
        // Vanilla: players within 500 m, icon bottom .2 m above the head. Vanilla draws it at half the HUD icon size; VR uses .3.
        private const double Range=500,Lift=.2,Size=.3;
        // SignalPainter draws character markers .55 of a two-degree icon; the gap is about a quarter degree.
        private static readonly float MarkerHalf=(float)(.55*Math.Tan(MarkerBillboard.IconAngle/2)),Gap=.004f;

        internal struct Talker { public long Entity; public Vector3D Position; }
        private static readonly RenderRecovery recovery=new RenderRecovery("VR voice markers");

        // Game thread, at frame commit, so the icons take the characters' committed positions; a failure only pauses them.
        internal static Talker[] Capture()
        {
            if(recovery.Failed) return null;
            try { var talkers=Talkers(); recovery.Succeeded(); return talkers; }
            catch(Exception ex) { recovery.Fail(ex,"VR voice markers paused"); return null; }
        }

        private static Talker[] Talkers()
        {
            var voice=MyVoiceChatSessionComponent.Static;
            var eye=MySector.MainCamera?.Position;
            if(voice==null || !eye.HasValue || Sync.Players==null) return null;
            List<Talker> talkers=null;
            foreach(var player in Sync.Players.GetOnlinePlayers())
            {
                var character=player.Character;
                if(character==null || character.IsDead || character.MarkedForClose || player.IsLocalPlayer) continue;
                var world=character.PositionComp.WorldMatrixRef;
                if(Vector3D.DistanceSquared(world.Translation,eye.Value)>Range*Range) continue;
                ulong id=player.Id.SteamId;
                if(MyGameService.GetPlayerMutedState(id)==MyPlayerChatState.Muted || !voice.PlayerTalking(id)) continue;
                (talkers ?? (talkers=new List<Talker>())).Add(new Talker {Entity=character.EntityId,
                    Position=world.Translation+world.Up*(character.PositionComp.LocalAABB.Height+Lift)});
            }
            return talkers?.ToArray();
        }

        // Render thread, with the marker head, up and facing. Sizes are in view tangent units at the talker's depth, as SignalPainter draws labels (y down).
        internal static void Draw(Texture2D target,Talker[] talkers,SignalLayout.Entry[] layout,MatrixD head,MatrixD view,MatrixD projection,bool faceViewer,Vector3D? up)
        {
            if(talkers==null) return;
            float size=(float)(Size*IconScreenHeight*2*MarkerBillboard.DesktopHalfField);
            var sprites=new List<NativeSprite>(talkers.Length);
            foreach(var talker in talkers)
            {
                if(!MarkerBillboard.TryCreate(talker.Position,head,up,faceViewer,out var board)) continue;
                double depth=Math.Max(.1,Vector3D.Dot(talker.Position-head.Translation,head.Forward));
                board.Scale=depth;
                float bottom=0;
                var entry=layout?.FirstOrDefault(e=>!e.Edge && e.Members.Any(m=>m.Entity==talker.Entity));
                if(entry!=null)
                {
                    // Far away the native lift is smaller than the name tag, so the icon rises to clear the name or the marker.
                    float top=entry.Labels.Length>0 ? entry.Labels.Min(l=>l.NameY)-entry.Point.Y:-MarkerHalf*entry.IconScale;
                    float below=(float)(-Vector3D.Dot(entry.Position-talker.Position,board.Up)/depth);
                    bottom=Math.Min(0,below+top-Gap);
                }
                var sprite=SignalPainter.Artwork(new NativeSprite(Icon,default(RectangleF),Vector4.One));
                if(board.Project(new RectangleF(-size/2,bottom-size,size,size),view,projection,ref sprite)) sprites.Add(sprite);
            }
            if(sprites.Count>0) NativeSprites.Draw(target,sprites.ToArray());
        }
    }
}
