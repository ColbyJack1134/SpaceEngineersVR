using System;
using System.Collections.Generic;
using System.Reflection;
using Sandbox.Game.Weapons;
using HarmonyLib;
using Sandbox.Game.Entities;
using Sandbox.Game.Screens;
using Sandbox.Game.World;
using Sandbox.Graphics.GUI;
using SharpDX.Direct3D11;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal static class RemoteCombat
    {
        internal sealed class HitView
        {
            internal long Owner;
            internal string Path;
            internal Vector4 Color;
            internal Vector2 Size;
            internal DateTime Time;
            internal Vector3D? Impact;
            internal Vector2 Angle;
        }
        internal static MyGuiControlImage HitControl;
        internal static HitView Hit;
        private static long hitOwner;
        private static Vector3D? hitImpact;
        private static Vector3D lastImpact;
        private static DateTime lastImpactTime;
        private static readonly Func<MyGunBase,bool> infiniteAmmo=AccessTools.MethodDelegate<Func<MyGunBase,bool>>(AccessTools.Method(typeof(MyGunBase),"HasInfiniteAmmo"));
        private static readonly Dictionary<Type,FieldInfo> burstFields=new Dictionary<Type,FieldInfo>();
        internal static FieldInfo BurstField(Type type)
        {
            if(!burstFields.TryGetValue(type,out var field)) burstFields[type]=field=AccessTools.Field(type,"m_shotsLeftInBurst");
            return field;
        }
        internal static int BurstCount(int remaining,int capacity,bool reloading,int available) => reloading ? 0:Math.Max(0,Math.Min(Math.Min(remaining,capacity),available));
        internal static void ReadAmmo(MyLargeTurretBase turret,out int remaining,out int capacity)
        {
            var gun=turret?.GunBase;
            remaining=gun?.CurrentAmmo ?? -1; capacity=gun?.CurrentAmmoMagazineDefinition?.Capacity ?? 0;
            var barrel=turret?.Barrel;
            if(gun==null || barrel==null || gun.ShotsInBurst<=0) return;
            var field=BurstField(barrel.GetType());
            if(field==null) return;
            // Timed turret reloads count burst shots independently of inventory-box rounds.
            capacity=gun.ShotsInBurst;
            int available=infiniteAmmo(gun) ? int.MaxValue:gun.GetTotalAmmunitionAmount();
            bool reloading=turret.GetGunReloadFactor()<1;
            int shots=(int)field.GetValue(barrel);
            // Clients reset the barrel counter only when the first post-reload shot fires.
            if(shots<=0 && !reloading && turret.ReloadCompletionTime>0) shots=capacity;
            remaining=BurstCount(shots,capacity,reloading,available);
        }
        private static OverlayCanvas ammo;
        private static ShaderResourceView ammoTexture;
        private static string ammoKey;
        private static long ammoRevision;
        internal static void BeginHit()
        {
            var session=MySession.Static;
            hitOwner=(session?.ControlledEntity as MyShipController)?.EntityId ?? 0;
            hitImpact=null;
            // The hit event has no position; on foot, use this player's latest bullet impact.
            if(hitOwner==0 && session?.LocalCharacter!=null && session.ControlledEntity==session.LocalCharacter &&
                (DateTime.UtcNow-lastImpactTime).TotalSeconds<.5)
            { hitOwner=session.LocalCharacter.EntityId; hitImpact=lastImpact; }
        }
        internal static void RecordImpact(ulong owner,Vector3D position)
        {
            if(owner!=0 && owner==Sandbox.Game.Multiplayer.Sync.MyId) { lastImpact=position; lastImpactTime=DateTime.UtcNow; }
        }
        internal static void Capture(MyHudWeaponHitIndicator indicator)
        {
            var image=indicator.GuiControlImage;
            HitControl=image;
            if(!image.Visible || hitOwner==0) { Hit=null; return; }
            var size=Sandbox.Graphics.MyGuiManager.GetScreenSizeFromNormalizedSize(image.Size);
            int height=Sandbox.Graphics.MyGuiManager.GetSafeFullscreenRectangle().Height;
            Hit=new HitView {Owner=hitOwner,Path=image.BackgroundTexture.Center.Texture,Color=image.ColorMask,Size=size,Time=DateTime.UtcNow,
                Impact=hitImpact,Angle=size*(float)(2*MarkerBillboard.DesktopHalfField/(height>0 ? height:1080))};
        }
        internal static NativeSprite HitSprite(HitView hit) =>
            new NativeSprite(hit.Path,default(RectangleF),hit.Color.ToLinearRGB()) {NativeGui=true,Premultiplied=true};
        // Constant angular size at any distance, including the native pop animation.
        internal static void DrawImpact(Texture2D target,MatrixD head,MatrixD view,MatrixD projection)
        {
            var hit=Hit;
            if(hit?.Impact==null || (DateTime.UtcNow-hit.Time).TotalSeconds>.3 ||
                !MarkerBillboard.TryCreate(hit.Impact.Value,head,head.Up,true,out var board)) return;
            var marker=HitSprite(hit); marker.IgnoreSceneDepth=true;
            double units=2*Math.Tan(MarkerBillboard.IconAngle/2)/(Plugin.Common.Config?.CrosshairScale ?? Config.PluginConfig.DefaultCrosshairScale);
            float width=(float)(hit.Angle.X/units),height=(float)(hit.Angle.Y/units);
            if(board.Project(new RectangleF(-width/2,-height/2,width,height),view,projection,ref marker))
                NativeSprites.Draw(target,new[] {marker});
        }
        internal static void DrawAmmo(Texture2D target,RemoteView.View view)
        {
            if(view.Ammo<0 || view.Capacity<=0) return;
            if(ammo==null)
            {
                ammo=new OverlayCanvas("Turret ammo",512,192,1,false,target.Device);
                ammoTexture=new ShaderResourceView(target.Device,ammo.Texture);
            }
            string key=view.Ammo+"/"+view.Capacity;
            if(key!=ammoKey || ammoRevision!=NativeSprites.Revision)
            {
                WeaponAmmo.Paint(ammo,WeaponAmmo.Counts(view.Ammo,view.Capacity)); ammo.Upload(); ammoKey=key; ammoRevision=NativeSprites.Revision;
            }
            float width=target.Description.Width*.18f,height=width*192/512;
            NativeSprites.Draw(target,new[] {new NativeSprite(null,new RectangleF(target.Description.Width-width-24,target.Description.Height-height-20,width,height),Vector4.One) {Texture=ammoTexture}});
        }
        internal static void Reset()
        {
            ammoTexture?.Dispose(); ammo?.Dispose(); ammoTexture=null; ammo=null; ammoKey=null;
            Hit=null; HitControl=null; hitOwner=0; hitImpact=null; lastImpactTime=default(DateTime);
        }
    }
    [HarmonyPatch(typeof(MyHudWeaponHitIndicator),"Hit")]
    internal static class HitFeedbackPatch
    {
        private static void Prefix() => RemoteCombat.BeginHit();
    }
    [HarmonyPatch(typeof(MyProjectile),nameof(MyProjectile.OnHit))]
    internal static class ProjectileImpactPatch
    {
        private static void Prefix(ref MyProjectile __instance,VRage.ModAPI.IMyEntity entity,ref VRage.Game.ModAPI.MyHitInfo hitInfo)
        {
            if(entity!=null) RemoteCombat.RecordImpact(__instance.OwningPlayer,hitInfo.Position);
        }
    }
    [HarmonyPatch(typeof(MyHudWeaponHitIndicator),"Update")]
    internal static class HitFeedbackUpdatePatch
    {
        private static void Postfix(MyHudWeaponHitIndicator __instance) => RemoteCombat.Capture(__instance);
    }
}
