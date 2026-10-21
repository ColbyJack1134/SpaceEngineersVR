using System;
using System.Reflection;
using HarmonyLib;
using Sandbox.Definitions;
using Sandbox.Game.Weapons;
using Sandbox.Game.World;
using SpaceEngineersVR.Plugin;
using SpaceEngineersVR.Util;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal static class PaintGun
    {
        private const string HandItem="PaintGun";
        private static Func<object> instance;
        private static ConstructorInfo material;
        // MyGuiScreenColorPicker is internal.
        private static readonly Type picker=typeof(MyPlayer).Assembly.GetType("Sandbox.Game.Gui.MyGuiScreenColorPicker");
        private static readonly PropertyInfo applyColor=picker==null ? null:AccessTools.Property(picker,"ApplyColor");
        private static readonly PropertyInfo applySkin=picker==null ? null:AccessTools.Property(picker,"ApplySkin");
        private static bool Native(PropertyInfo apply) => apply?.GetValue(null) is bool on ? on:true;
        private static void Native(PropertyInfo apply,bool on) => apply?.SetValue(null,on);
        internal static void Bind(Func<object> mod,Type paintMaterial)
        {
            instance=mod;
            material=paintMaterial==null ? null:AccessTools.Constructor(paintMaterial,new[] {typeof(Vector3?),typeof(MyStringHash?)});
            if(mod==null) Reset();
        }
        internal static void Reset()
        {
            Firing=Deep=hasAim=false; synced=null;
        }

        // Set while the mod's tool update runs.
        [ThreadStatic] internal static bool Aiming;
        internal static bool Firing,Deep;
        private static MatrixD aim;
        private static bool hasAim;

        private static object Mod => instance?.Invoke();
        private static object Palette => ModReflection.Get(Mod,"Palette");
        private static object Local => ModReflection.Get(Palette,"LocalInfo");
        private static object Tool => ModReflection.Get(Mod,"LocalToolHandler");
        private static bool Flag(object owner,string name) => ModReflection.Value(owner,false,name);

        internal static bool Bound => instance!=null;
        internal static bool Equipped
        {
            get
            {
                var character=MySession.Static?.LocalCharacter;
                return Bound && character!=null && MySession.Static.ControlledEntity==character &&
                    character.CurrentWeapon is MyAutomaticRifleGun gun && gun.DefinitionId.SubtypeName==HandItem && ModReflection.Get(Tool,"LocalTool")!=null;
            }
        }

        // Third person keeps head aim; the observer hand can be far away.
        internal static bool TryAim(out MatrixD pose)
        {
            pose=aim;
            if(!Aiming || !Main.VrActive || ThirdPersonView.Active || SpectatorView.Active) return false;
            // The wheel and menus can take the gun hand.
            if(InputRouter.RadialOpen || Main.MenuOpen) return hasAim;
            var character=MySession.Static?.LocalCharacter;
            if(!(WeaponHandling.Profile is WeaponProfile profile) || !WeaponHandling.TryPose(character,out var model,out _)) return hasAim=false;
            pose=aim=Multiplayer.HeldItemPose.Working(model,profile);
            return hasAim=true;
        }

        private sealed class Synced
        {
            internal string Skin;
            internal MyStringHash ModSkin;
            internal bool Color,Apply,ModColor,ModApply;
            internal int Slot,ModSlot;
        }
        private static Synced synced;
        // A change since the last frame wins, native side first.
        internal static void Update()
        {
            var local=Local;
            var player=MySession.Static?.LocalHumanPlayer;
            if(local==null || player==null) { synced=null; return; }
            string skin=player.BuildArmorSkin ?? string.Empty;
            var modSkin=ModReflection.Value(local,MyStringHash.NullOrEmpty,"SelectedSkin");
            if(synced==null || skin!=synced.Skin) { if(Owned(skin)) ModReflection.Set(local,"SelectedSkin",MyStringHash.GetOrCompute(skin)); }
            else if(modSkin!=synced.ModSkin) player.BuildArmorSkin=modSkin.String;
            bool color=Native(applyColor),apply=Native(applySkin);
            bool modColor=Flag(local,"ApplyColor"),modApply=Flag(local,"ApplySkin");
            if(synced==null || color!=synced.Color) ModReflection.Set(local,"ApplyColor",color);
            else if(modColor!=synced.ModColor) Native(applyColor,modColor);
            if(synced==null || apply!=synced.Apply) ModReflection.Set(local,"ApplySkin",apply);
            else if(modApply!=synced.ModApply) Native(applySkin,modApply);
            int slot=player.SelectedBuildColorSlot,modSlot=ModReflection.Value(local,slot,"SelectedColorSlot");
            if(synced==null || slot!=synced.Slot) ModReflection.Set(local,"SelectedColorSlot",slot);
            else if(modSlot!=synced.ModSlot) player.SelectedBuildColorSlot=modSlot;
            synced=new Synced {
                Skin=player.BuildArmorSkin ?? string.Empty,ModSkin=ModReflection.Value(local,MyStringHash.NullOrEmpty,"SelectedSkin"),
                Color=Native(applyColor),Apply=Native(applySkin),ModColor=Flag(local,"ApplyColor"),ModApply=Flag(local,"ApplySkin"),
                Slot=player.SelectedBuildColorSlot,ModSlot=ModReflection.Value(local,player.SelectedBuildColorSlot,"SelectedColorSlot")};
        }
        // The mod resets unowned or hidden skins.
        private static bool Owned(string skin)
        {
            if(skin.Length==0) return true;
            var info=Invoke(Palette,"GetSkinInfo",new[] {typeof(MyStringHash)},MyStringHash.GetOrCompute(skin));
            return info!=null && ModReflection.Value(info,false,"Selectable");
        }

        private static object Invoke(object owner,string name,Type[] parameters,params object[] arguments) => ModReflection.Invoke(owner,name,parameters,arguments);
        private static bool Access(string name) => Invoke(Mod,name,new[] {typeof(ulong?)},new object[] {null}) is bool allowed && allowed;
        private static void Notify(string text) => Invoke(ModReflection.Get(Mod,"Notifications"),"Show",new[] {typeof(int),typeof(string),typeof(string),typeof(int)},0,text,"Red",2000);

        private static void PickFromTarget()
        {
            if(material==null) return;
            var tool=Tool;
            object paint=null;
            if(ModReflection.Get(tool,"AimedBlock") is IMySlimBlock block)
                paint=material.Invoke(new object[] {(Vector3?)block.ColorMaskHSV,(MyStringHash?)block.SkinSubtypeId});
            else if(ModReflection.Get(tool,"AimedPlayer")!=null) paint=ModReflection.Get(tool,"AimedPlayersPaint");
            if(paint==null) { Notify("First aim at a block or player."); return; }
            var palette=Palette;
            ModReflection.Set(palette,"ColorPickMode",false);
            Invoke(palette,"GrabPaletteFromPaint",new[] {material.DeclaringType,typeof(bool)},paint,false);
        }
        private static void TogglePickMode()
        {
            var palette=Palette;
            bool on=!Flag(palette,"ColorPickMode");
            ModReflection.Set(palette,"ColorPickMode",on);
            if(on) ModReflection.Set(palette,"ReplaceMode",false);
        }
        private static void ToggleReplace()
        {
            var palette=Palette;
            if(!Access("AccessReplaceColor")) { Notify(ModReflection.Value(Mod,"Replace color mode is not available.","ReplaceColorAccessInfo")); return; }
            bool on=!Flag(palette,"ReplaceMode");
            ModReflection.Set(palette,"ReplaceMode",on);
            if(on) ModReflection.Set(palette,"ColorPickMode",false);
        }

        // Same conversion as the native toolbar's color panel.
        internal static string Swatch(Vector3 mask)
        {
            var rgb=new Vector3(mask.X,MathHelper.Clamp(mask.Y+.8f,0,1),MathHelper.Clamp(mask.Z+.55f,0,1)).HSVtoColor();
            return NativeSprites.Swatch(System.Drawing.Color.FromArgb(rgb.R,rgb.G,rgb.B));
        }
        internal static string SkinIcon(string skin)
        {
            var icons=string.IsNullOrEmpty(skin) ? null:MyDefinitionManager.Static?.GetAssetModifierDefinition(new MyDefinitionId(typeof(MyObjectBuilder_AssetModifierDefinition),skin))?.Icons;
            return icons!=null && icons.Length>0 ? icons[0]:@"Textures\Sprites\Cross.dds";
        }
        private static string Swatch() => Swatch(MySession.Static?.LocalHumanPlayer?.SelectedBuildColor ?? MyPlayer.DefaultBuildColorSlots[0]);
        private static string SkinIcon() => SkinIcon(MySession.Static?.LocalHumanPlayer?.BuildArmorSkin);
        private static string State(bool on) => on ? "on":"off";

        private static ActionChoice[] actions;
        internal static ActionChoice[] Actions() => Equipped ? Choices() : Array.Empty<ActionChoice>();
        internal static ActionChoice[] Choices() => actions ?? (actions=new[] {
            new ActionChoice(()=>"Palette",()=>GameActions.PaletteAction.Run(),historyKey:"Paint gun palette",opensMenu:true,icon:Swatch,subIcon:SkinIcon),
            new ActionChoice(()=>"Apply color: "+State(Native(applyColor)),()=>Native(applyColor,!Native(applyColor)),
                historyKey:"Apply color",icon:()=>Native(applyColor) ? Swatch():NativeSprites.Dim(Swatch())),
            new ActionChoice(()=>"Apply skin: "+State(Native(applySkin)),()=>Native(applySkin,!Native(applySkin)),
                historyKey:"Apply skin",icon:()=>Native(applySkin) ? SkinIcon():NativeSprites.Dim(SkinIcon())),
            new ActionChoice("Pick from target",PickFromTarget,icon:NativeSprites.Hud("CopyGrid")),
            new ActionChoice(()=>"Pick mode: "+State(Flag(Palette,"ColorPickMode")),TogglePickMode,historyKey:"Pick mode",icon:()=>NativeSprites.Hud("ColorPicker")),
            new ActionChoice(()=>"Replace mode: "+State(Flag(Palette,"ReplaceMode")),ToggleReplace,historyKey:"Replace mode",icon:()=>NativeSprites.Hud("MultiBlockBuilding")),
            new ActionChoice(()=>"Replace on: "+(Flag(Palette,"ReplaceShipWide") ? "ship":"grid"),()=>ModReflection.Set(Palette,"ReplaceShipWide",!Flag(Palette,"ReplaceShipWide")),
                enabled:()=>Flag(Palette,"ReplaceMode"),historyKey:"Replace on",icon:()=>NativeSprites.Hud("ToggleConnectedGrid")),
            new ActionChoice(()=>"Symmetry: "+State(Sandbox.ModAPI.MyAPIGateway.CubeBuilder?.UseSymmetry==true),()=>{ var builder=Sandbox.ModAPI.MyAPIGateway.CubeBuilder; if(builder!=null) builder.UseSymmetry=!builder.UseSymmetry; },
                enabled:()=>Access("AccessSymmetry"),historyKey:"Paint symmetry",icon:()=>NativeSprites.Hud("Symmetry"))});
    }
}
