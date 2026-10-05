using System;
using System.Collections.Generic;
using Sandbox.Game.Weapons;
using Sandbox.Game.World;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal static class WeaponAmmo
    {
        internal static string Count(MyGunBase gun) => gun?.CurrentAmmo.ToString("N0") ?? "";
        internal static SurfaceView Label(WeaponProfile profile,MatrixD model,int remaining,int capacity)
        {
            var point=profile.Kind==ItemKind.Pistol ? new Vector3D(profile.Item=="FlareGunItem" ? -.030:-.025,.057,.025):
                profile.Kind==ItemKind.Launcher ? new Vector3D(-.070,.125,.065):new Vector3D(-.038,.047,-.012);
            var local=MatrixD.CreateWorld(point,Vector3D.Right,Vector3D.Up);
            return new SurfaceView {Id="WeaponAmmo",Style=SurfaceStyle.Ammo,Text=remaining+" / "+capacity,
                Levels=new[] {capacity>0 ? (float)remaining/capacity:0},Width=.043f,Height=.016f,
                ParentLocal=local,Pose=local*model};
        }
        internal static bool Inspecting(MatrixD model,MatrixD head,MatrixD label)
        {
            var toEye=head.Translation-label.Translation; double distance=toEye.Length();
            return distance>.18 && distance<.85 && Vector3D.Dot(label.Backward,toEye/distance)>.55 && Vector3D.Dot(model.Forward,head.Forward)<.85;
        }
        internal static SurfaceView View()
        {
            var character=MySession.Static?.LocalCharacter; var profile=WeaponHandling.Profile;
            if(profile==null || profile.Tool || !InputRouter.Gameplay || Plugin.Main.MenuOpen ||
                !(character?.CurrentWeapon is MyAutomaticRifleGun gun) || !WeaponHandling.TryPose(character,out var model,out _)) return null;
            var magazine=gun.GunBase?.CurrentAmmoMagazineDefinition; if(magazine==null) return null;
            var view=Label(profile,model,gun.GunBase.CurrentAmmo,magazine.Capacity);
            view.RenderParent=gun.Render.GetRenderObjectID();
            return Inspecting(model,character.GetHeadMatrix(true,true),view.Pose) ? view:null;
        }
        internal static void Paint(OverlayCanvas canvas,SurfaceView view)
        {
            canvas.Clear(System.Drawing.Color.Transparent);
            float fraction=view.Levels[0];
            var color=fraction<=.1f ? new Color(255,88,72):fraction<=.25f ? new Color(255,210,70):Color.White;
            var counts=view.Text.Split('/');
            string remaining=counts[0].Trim(),capacity="/ "+counts[1].Trim();
            float width=SignalFont.Width(remaining,143)+22+SignalFont.Width(capacity,82);
            float scale=Math.Min(1,480/width),x=(512-width*scale)/2;
            var glyphs=new List<NativeSprite>();
            SignalFont.Add(glyphs,remaining,x,137-143*30f/37*scale,143*scale,480,color.ToVector4(),512,192,font:"White");
            x+=(SignalFont.Width(remaining,143)+22)*scale;
            SignalFont.Add(glyphs,capacity,x,137-82*30f/37*scale,82*scale,480,new Color(165,186,197).ToVector4(),512,192,font:"White");
            foreach(var glyph in glyphs)
                for(int side=0;side<4;side++)
                {
                    var edge=glyph; var bounds=edge.Bounds;
                    bounds.X+=side<2 ? (side==0 ? -2:2):0;
                    bounds.Y+=side>=2 ? (side==2 ? -2:2):0;
                    edge.Bounds=bounds; edge.Tint=new Vector4(0,0,0,.75f); canvas.Sprite(edge);
                }
            foreach(var glyph in glyphs)
            {
                var text=glyph; text.EncodeSrgb=true; canvas.Sprite(text);
            }
        }
    }
}
