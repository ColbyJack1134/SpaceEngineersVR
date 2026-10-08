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
        }
        internal static MyGuiControlImage HitControl;
        internal static HitView Hit;
        private static long hitOwner;
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
            hitOwner=(MySession.Static?.ControlledEntity as MyShipController)?.EntityId ?? 0;
        }
        internal static void Capture(MyHudWeaponHitIndicator indicator)
        {
            var image=indicator.GuiControlImage;
            HitControl=image;
            Hit=image.Visible && hitOwner!=0 ? new HitView {Owner=hitOwner,Path=image.BackgroundTexture.Center.Texture,
                Color=image.ColorMask,Size=Sandbox.Graphics.MyGuiManager.GetScreenSizeFromNormalizedSize(image.Size),Time=DateTime.UtcNow}:null;
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
            Hit=null; HitControl=null; hitOwner=0;
        }
    }
    [HarmonyPatch(typeof(MyHudWeaponHitIndicator),"Hit")]
    internal static class HitFeedbackPatch
    {
        private static void Prefix() => RemoteCombat.BeginHit();
    }
    [HarmonyPatch(typeof(MyHudWeaponHitIndicator),"Update")]
    internal static class HitFeedbackUpdatePatch
    {
        private static void Postfix(MyHudWeaponHitIndicator __instance) => RemoteCombat.Capture(__instance);
    }
}
