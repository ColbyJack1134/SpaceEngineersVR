#if SEVR_DIAGNOSTICS
using System;
using System.IO;
using System.Runtime.Serialization;
using Sandbox.Game.Entities.Character.Components;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using HarmonyLib;
using Sandbox.Game.Entities.Cube;
using SpaceEngineersVR.Patches;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Plugin;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    public static class PaintGunTests
    {
        private static void Require(bool value,string message) { if(!value) throw new Exception(message); }
        internal static string Data => Path.GetFullPath(Path.Combine(VRage.FileSystem.MyFileSystem.ContentPath,"..","..","..","workshop","content","244850","500818376","Data"));
        // Stand-in for the mod's layout.
        private sealed class Mod { public static Mod Instance=new Mod(); }
        private sealed class LocalToolHandler
        {
            internal bool AimingInside,IronsightInside;
            [MethodImpl(MethodImplOptions.NoInlining)]
            private void UpdateInput(bool anyKeyOrMouse,bool inMenu,bool paused)
            {
                AimingInside=PaintGun.Aiming;
                IronsightInside=((MyCharacterWeaponPositionComponent)FormatterServices.GetUninitializedObject(typeof(MyCharacterWeaponPositionComponent))).IsInIronSight;
            }
            internal void Run() => UpdateInput(false,false,false);
        }
        private sealed class GameConfig
        {
            public bool UsingGamepad;
            [MethodImpl(MethodImplOptions.NoInlining)] private void UpdateInput(bool anyKeyOrMouse,bool inMenu,bool paused) { UsingGamepad=true; }
            internal void Run() => UpdateInput(false,false,false);
        }
        private sealed class SelectionGUI
        {
            internal bool Visible=true;
            [MethodImpl(MethodImplOptions.NoInlining)] internal void SetGUIVisible(bool set) { Visible=set; }
        }
        private sealed class PaletteHUD
        {
            internal bool Drawn;
            [MethodImpl(MethodImplOptions.NoInlining)] internal void DrawPaletteHUD() { Drawn=true; }
        }
        private struct PaintMaterial
        {
            public readonly Vector3? ColorMask;
            public readonly MyStringHash? Skin;
            public PaintMaterial(Vector3? colorMask,MyStringHash? skin) { ColorMask=colorMask; Skin=skin; }
        }
        private sealed class ColorPickerGUIWarning
        {
            internal bool Drawn;
            [MethodImpl(MethodImplOptions.NoInlining)] internal void DrawText() { Drawn=true; }
        }
        private sealed class Notifications
        {
            internal string Last;
            [MethodImpl(MethodImplOptions.NoInlining)] public void Show(int line,string text,string font="Debug",int aliveTime=200) { Last=text; }
        }
        private static Type Layout(string name)
        {
            switch(name)
            {
                case "Features.Tool.LocalToolHandler": return typeof(LocalToolHandler);
                case "Features.Tool.SelectionGUI": return typeof(SelectionGUI);
                case "Systems.GameConfig": return typeof(GameConfig);
                case "Features.Palette.PaletteHUD": return typeof(PaletteHUD);
                case "Features.Palette.PaintMaterial": return typeof(PaintMaterial);
                case "Features.ColorPickerGUIWarning": return typeof(ColorPickerGUIWarning);
                case "Features.Notifications": return typeof(Notifications);
                default: return null;
            }
        }

        internal static void Run(Action<string> log)
        {
            Require(AccessTools.Field(typeof(MyDefaultPlacementProvider),"m_lastUpdate")?.FieldType==typeof(int),"Native placement ray cache changed");
            var picker=typeof(Sandbox.Game.World.MyPlayer).Assembly.GetType("Sandbox.Game.Gui.MyGuiScreenColorPicker");
            foreach(string name in new[] {"ApplyColor","ApplySkin"})
            {
                var property=picker==null ? null:AccessTools.Property(picker,name);
                Require(property?.PropertyType==typeof(bool) && property.GetGetMethod(true)?.IsStatic==true && property.GetSetMethod(true)!=null,"Native palette switch changed: "+name);
            }
            Fixture();
            InstalledMod(log);
            log("PASS paint gun: adapter patches attach to the mod layout, VR hides its palette, picker note and status panel, keeps red notices only, " +
                "reads grip as deep paint only inside the tool update, keeps the trigger off the gamepad axis, scopes the gun aim and detaches on reset; native palette switches and ray cache present");
        }

        private static void Fixture()
        {
            var failed=AccessTools.Field(typeof(Main),"failed");
            bool previous=(bool)failed.GetValue(null);
            try
            {
                var harmony=new Harmony("SpaceEngineersVR.PaintGunTests");
                harmony.CreateClassProcessor(typeof(PaintGunDeepPatch)).Patch();
                PaintGunPatches.Attach(typeof(Mod),Layout,harmony);
                Require(PaintGun.Bound,"Paint gun adapter did not bind to the mod layout");
                var tool=new LocalToolHandler();
                var selection=new SelectionGUI();
                var hud=new PaletteHUD();
                var warning=new ColorPickerGUIWarning();
                var notices=new Notifications();
                failed.SetValue(null,false);
                tool.Run();
                Require(tool.AimingInside && !PaintGun.Aiming,"Paint gun aim not scoped to the mod's tool update");
                hud.DrawPaletteHUD(); warning.DrawText(); selection.SetGUIVisible(true);
                Require(!hud.Drawn && !warning.Drawn && !selection.Visible,"Paint gun desktop HUD still drawn in VR");
                notices.Show(0,"Color pick mode turned off.","Debug",2000);
                Require(notices.Last==null,"Paint gun prompt reached the VR notices");
                notices.Show(0,"Can't paint enemy ships.","Red",2000);
                Require(notices.Last=="Can't paint enemy ships.","Paint gun error was dropped");
                PaintGun.Deep=true; tool.Run();
                Require(tool.IronsightInside,"Grip did not select deep paint");
                PaintGun.Deep=false; tool.Run();
                Require(!tool.IronsightInside,"Deep paint without the grip");
                PaintGun.Deep=true;
                Require(!((MyCharacterWeaponPositionComponent)FormatterServices.GetUninitializedObject(typeof(MyCharacterWeaponPositionComponent))).IsInIronSight,"Deep paint leaked outside the mod's tool update");
                var config=new GameConfig(); config.Run();
                Require(!config.UsingGamepad,"Paint gun trigger can switch to a gamepad axis in VR");
                failed.SetValue(null,true);
                hud.Drawn=false; hud.DrawPaletteHUD();
                Require(hud.Drawn,"Paint gun HUD hidden outside VR");
                PaintGunPatches.Reset();
                failed.SetValue(null,false);
                hud.Drawn=false; hud.DrawPaletteHUD();
                Require(hud.Drawn,"Paint gun patches remained after reset");
            }
            finally { PaintGunPatches.Reset(); new Harmony("SpaceEngineersVR.PaintGunTests").UnpatchAll("SpaceEngineersVR.PaintGunTests"); PaintGun.Deep=false; failed.SetValue(null,previous); }
        }

        public static void Export(string game,string output,Action<string> log)
        {
            Directory.CreateDirectory(output); UiTests.Initialize(game,Path.Combine(output,"data"));
            var picker=typeof(Sandbox.Game.World.MyPlayer).Assembly.GetType("Sandbox.Game.Gui.MyGuiScreenColorPicker");
            var color=AccessTools.Property(picker,"ApplyColor"); var skin=AccessTools.Property(picker,"ApplySkin");
            // Diagnostics load no skin definitions.
            const string skinned=@"Textures\GUI\Icons\Skins\Armor\CarbonFiberArmor.dds";
            Require(File.Exists(Path.Combine(VRage.FileSystem.MyFileSystem.ContentPath,skinned)),"Installed skin icon missing: "+skinned);
            var choices=PaintGun.Choices();
            using(var device=new SharpDX.Direct3D11.Device(SharpDX.Direct3D.DriverType.Warp,SharpDX.Direct3D11.DeviceCreationFlags.BgraSupport))
            using(var wheel=new OverlayCanvas("Paint gun wheel",1024,1024,1,false,device))
            {
                void Page(string name,int selected,Func<string,string> skinIcon)
                {
                    var view=new ToolbarWheel.View {Title="Paint gun",Group=1,Pages=3,Page=0,Selected=selected,
                        Labels=choices.Select(c=>c.Label).ToArray(),Enabled=choices.Select(c=>c.Enabled).ToArray(),
                        Icons=choices.Select(c=>new[] {skinIcon(c.Icon)}).ToArray(),SubIcons=choices.Select(c=>skinIcon(c.SubIcon)).ToArray(),ItemText=new string[choices.Length]};
                    UiTests.Render(wheel,()=>ToolbarWheel.Paint(wheel,view));
                    UiTests.Save(wheel.Texture,Path.Combine(output,"paint-gun-wheel-"+name+".png"));
                }
                string none=PaintGun.SkinIcon(null),owned=skinned;
                var slots=Sandbox.Game.World.MyPlayer.DefaultBuildColorSlots;
                string grey=PaintGun.Swatch(slots[0]),blue=PaintGun.Swatch(slots[2]);
                string Skinned(string icon) => icon?.Replace(none,owned).Replace(grey,blue);
                try
                {
                    Page("default",0,icon=>icon);
                    Page("skin",0,Skinned);
                    color.SetValue(null,false); skin.SetValue(null,false);
                    Page("off",1,Skinned);
                }
                finally { color.SetValue(null,true); skin.SetValue(null,true); }
            }
            log("PASS production paint gun wheel renders: default, skinned and switches off");
        }

        private static void InstalledMod(Action<string> log)
        {
            string folder=Data;
            if(!Directory.Exists(folder)) { log("SKIP paint gun source contract: mod not installed"); return; }
            string Read(string file) => File.ReadAllText(Path.Combine(folder,file));
            void Contains(string file,params string[] patterns)
            {
                string source=Read(file);
                foreach(string pattern in patterns) Require(Regex.IsMatch(source,pattern),"Paint Gun "+file+" no longer matches the adapter: "+pattern);
            }
            Contains(@"Tool.sbc",@"<Id Type=""MyObjectBuilder_AutomaticRifle"" Subtype=""PaintGun"" />");
            const string scripts=@"Scripts\PaintGun\";
            Contains(scripts+"PaintGunMod.cs",@"namespace Digi\.PaintGun\b",@"public Palette Palette;",@"public LocalToolHandler LocalToolHandler;",@"public Notifications Notifications;",
                @"public bool AccessReplaceColor\(ulong\? steamId\)",@"public bool AccessSymmetry\(ulong\? steamId\)",@"public readonly string ReplaceColorAccessInfo");
            Contains(scripts+@"Libraries\ComponentLib\ModBase.cs",@"public static TModMain Instance;");
            Contains(scripts+@"Features\Tool\LocalToolHandler.cs",@"namespace Digi\.PaintGun\.Features\.Tool",@"protected override void UpdateInput\(bool anyKeyOrMouse, bool inMenu, bool paused\)",
                @"InputWrapper\.IsControlPressed\(MyControlsSpace\.PRIMARY_TOOL_ACTION\)",@"character\.GetHeadMatrix\(false, true\)",@"MyAPIGateway\.CubeBuilder\.FindClosestGrid\(\)",
                @"Utils\.IsAimingDownSights\(character\)",@"Main\.GameConfig\.UsingGamepad\)",@"trigger = InputWrapper\.IsControlPressed",@"public PaintGunItem LocalTool;",@"public IMySlimBlock AimedBlock;",@"public IMyPlayer AimedPlayer;",@"public PaintMaterial AimedPlayersPaint;");
            Contains(scripts+@"Libraries\InputWrapper.cs",@"return control\.IsPressed\(\);");
            Contains(scripts+@"Utilities\Utils.cs",@"namespace Digi\.PaintGun\.Utilities",@"public static bool IsAimingDownSights\(IMyCharacter character\)",@"weaponPosComp\.IsInIronSight");
            Contains(scripts+@"Systems\GameConfig.cs",@"namespace Digi\.PaintGun\.Systems",@"public bool UsingGamepad;",@"protected override void UpdateInput\(bool anyKeyOrMouse, bool inMenu, bool paused\)",@"UsingGamepad = newUsingGamepad;");
            Contains(scripts+@"Features\Palette\PaletteHUD.cs",@"namespace Digi\.PaintGun\.Features\.Palette",@"void DrawPaletteHUD\(\)");
            Contains(scripts+@"Features\ColorPickerGUIWarning.cs",@"namespace Digi\.PaintGun\.Features\b",@"void DrawText\(\)");
            Contains(scripts+@"Features\Tool\SelectionGUI.cs",@"void SetGUIVisible\(bool set\)");
            Contains(scripts+@"Features\Notifications.cs",@"namespace Digi\.PaintGun\.Features\b",@"public void Show\(int line, string text, string font = MyFontEnum\.Debug, int aliveTime");
            Contains(scripts+@"Features\Palette\Palette.cs",@"public PlayerInfo LocalInfo",@"public bool ReplaceMode\b",@"public bool ReplaceShipWide\b",@"public bool ColorPickMode\b",
                @"public SkinInfo GetSkinInfo\(MyStringHash skinSubtypeId\)",@"public void GrabPaletteFromPaint\(PaintMaterial pickedMaterial, bool changeApply = false\)");
            Contains(scripts+@"Features\Palette\PlayerInfo.cs",@"public int SelectedColorSlot\b",@"public MyStringHash SelectedSkin\b",@"public bool ApplyColor\b",@"public bool ApplySkin\b");
            Contains(scripts+@"Features\Palette\SkinInfo.cs",@"public bool LocallyOwned;");
            Contains(scripts+@"Features\Palette\DataObjects.cs",@"public PaintMaterial\(Vector3\? colorMask, MyStringHash\? skin\)");
            log("PASS paint gun source contract: installed mod members, head aim, fire control read and deep paint check match the adapter");
        }
    }
}
#endif
