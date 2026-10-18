using SpaceEngineersVR.Player.Components;
using HarmonyLib;
using Sandbox.Game;
using Sandbox.Game.World;
using Sandbox.Graphics.GUI;
using SpaceEngineersVR.Config;
using SpaceEngineersVR.GUI;
using SpaceEngineersVR.Wrappers;
using System;
using System.IO;
using System.Reflection;
using Valve.VR;
using VRage.FileSystem;
using VRage.Plugins;
using VRage.Utils;
using VRageMath;
using SpaceEngineersVR.Diagnostics;
using System.Runtime.InteropServices;
using VRage.Input;

namespace SpaceEngineersVR.Plugin
{
    // ReSharper disable once UnusedType.Global
    public class Main : IPlugin
    {
        public Harmony Harmony { get; private set; }
        public PluginConfig Config => config?.Data;

        private PersistentConfig<PluginConfig> config;
        private static string ConfigFileName = $"{Common.Name}.cfg";

        private static volatile bool failed = true;
        public static bool VrActive => !failed;
        internal static bool MenuFallback => menuFallback;
        internal static bool VrPresent => !failed || menuFallback;
        public static volatile bool MenuOpen = true;
        public static volatile bool ShowDesktopPanel;
        public static volatile bool WorldAvailable;
        private static volatile bool cleanupRequested;
        private static volatile bool menuFallback;
        private static object failedSession,failedCharacter,failedControl;
        private static bool fallbackSawDialog;
        private static readonly Integration touchScreens=new Integration("TouchScreenAPI",Player.TouchScreenBridge.Reset,Player.TouchScreenBridge.RefreshRegistrations,Player.TouchScreenBridge.Update,Player.TouchScreenBridge.Draw),
            arthurLcd=new Integration("Arthur's LCD",Player.ArthurLcdBridge.Reset,Player.ArthurLcdBridge.RefreshRegistrations,Player.ArthurLcdBridge.Update,Player.ArthurLcdBridge.Draw);
        internal static readonly Integration[] Integrations={touchScreens,arthurLcd,
            new Integration("Mod HUD",Patches.ModHud.Reset,Patches.ModHud.RefreshRegistrations),
            new Integration("Voxel tool mods",Patches.DigiTools.Reset,Patches.DigiTools.RefreshRegistrations)};
        private static int recoveryAttempts;
        private static DateTime recoveredAt;
        public static void Fail(Exception error, string context)
        {
            menuFallback = false;
            failed = true;
            cleanupRequested = true;
            Logger.Critical(error, context);
            MyLog.Default.WriteLine("SEVR: " + context + ": " + error);
        }
        internal static void FailToMenu(Exception error, string context)
        {
            if(failed) { Fail(error, context); return; }
            var session=MySession.Static;
            failedSession=session; failedCharacter=session?.LocalCharacter; failedControl=session?.ControlledEntity;
            fallbackSawDialog=false;
            WorldAvailable=false; MenuOpen=true; menuFallback=true; failed=true; cleanupRequested=true;
            Logger.Critical(error, context+"; VR menu remains available");
            MyLog.Default.WriteLine("SEVR: " + context + ": " + error);
        }
        private bool openVrInitialized;

        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string path);

        private Vector2I DesktopResolution;

        public void LoadAssets(string folder)
        {
            Common.SetAssetPath(folder);
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public void Init(object gameInstance)
        {
            MyLog.Default.WriteLine("SpaceEngineersVR: starting...");
            var configPath = Path.Combine(MyFileSystem.UserDataPath, ConfigFileName);
            config = PersistentConfig<PluginConfig>.Load(configPath);
            config.Data.InitializeHudProfiles(config.Created);

            try
            {
                Common.SetPlugin(this);
                // Native dependencies are staged next to this plugin, not in the game's Bin64.
                var nativePath = Path.Combine(Path.GetDirectoryName(typeof(Main).Assembly.Location), "openvr_api.dll");
                if (LoadLibrary(nativePath) == IntPtr.Zero)
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Cannot load " + nativePath);

                bool compatible = CompatibilityProbe.Run(line => Logger.Info(line));
#if SEVR_DIAGNOSTICS
                string mode=Environment.GetEnvironmentVariable("SEVR_MODE")?.Trim().ToLowerInvariant();
                if (mode == "physicaltest")
                {
                    Harmony=new Harmony(Common.Name);
                    Harmony.PatchAll(Assembly.GetExecutingAssembly());
                    Player.StereoFoliage.Warm();
                    PhysicalRendererProbe.Start(Harmony);
                    return;
                }
                if (mode == "patchtest")
                {
                    Harmony = new Harmony(Common.Name);
                    try
                    {
                        Harmony.PatchAll(Assembly.GetExecutingAssembly());
                        Patches.FrameInjections.Install(Harmony);
                        Logger.Info("PATCH SMOKE TEST PASSED: render, presentation, input and motion-tool hooks attached in-game; VR execution remained disabled.");
                    }
                    finally { Harmony.UnpatchAll(Common.Name); }
                    return;
                }
                if (mode == "diagnostics")
                {
                    VrProbe.Run(line => Logger.Info(line));
                    Logger.Info("Diagnostic mode complete. Rendering and motion-control patches are disabled.");
                    return;
                }
#endif
                if (!compatible)
                    throw new InvalidOperationException("Renderer compatibility checks failed. See the SpaceEngineersVR log.");
                failed = !Initialize();
            }
            catch (Exception ex)
            {
                failed = true;
                MyLog.Default.WriteLine("SpaceEngineersVR: Failed to start!");
                MyLog.Default.WriteLine(ex.ToString());
                return;
            }
        }

        public void Dispose()
        {
            Player.StereoExposure.Reset();
            Player.StereoParticles.Dispose();
            Multiplayer.MultiplayerSupport.Stop();
            Player.ThirdPersonView.Reset();
            Player.NativeGloves.Reset();
            Player.MenuPointer.Release();
            foreach(var integration in Integrations) integration.Reset();
            Player.CockpitButtons.Reset();
            Player.BodyProximity.Reset();
            Player.TrackedArms.Reset();
            Player.CockpitControls.Reset();
            config?.Dispose();
            if (openVrInitialized)
            {
                try
                {
                    OpenVR.System?.AcknowledgeQuit_Exiting();
                    Logger.Info("Exiting OpenVR and closing threads");
                    MySession.AfterLoading -= AfterLoadedWorld;
                    MySession.OnUnloading -= UnloadingWorld;
                    Harmony?.UnpatchAll(Common.Name);
                    OpenVR.Shutdown();
                    openVrInitialized = false;
                }
                catch (Exception ex)
                {
                    Logger.Critical(ex, "Dispose failed");
                }
            }
        }

        public void Update()
        {
            if (Harmony != null)
                try { Patches.DoubleClickTolerancePatch.Install(Harmony); }
                catch (Exception ex) { Logger.Warning(ex, "Double-click tolerance patch skipped; native double clicks keep the default radius"); }
#if SEVR_DIAGNOSTICS
            if (PhysicalRendererProbe.Active) { PhysicalRendererProbe.Update(); return; }
#endif
            Multiplayer.MultiplayerRuntime.Update();
            if (cleanupRequested)
            {
                cleanupRequested = false;
                try
                {
                    Player.InputRouter.Reset();
                    Player.ToolbarWheel.Close(resume:false);
                    Player.MenuKeyboard.Close();
                    Player.BodyProximity.Reset();
                    Player.TrackedArms.Reset();
                    Player.CockpitControls.Reset();
                    foreach(var integration in Integrations) integration.Reset();
                    Player.CockpitButtons.Reset();
                    Player.ThirdPersonView.Reset();
                    Player.NativeGloves.Reset();
                    if(!menuFallback) VRGUIManager.Hide();
                    Player.EssentialHud.Hide();
                    Player.BuildOrientationHud.Hide();
                    Player.PerformanceHud.Hide();
                    Player.ToolbarWheel.Hide();
                }
                catch (Exception ex) { Logger.Critical(ex, "VR failure cleanup"); }
            }
            if(menuFallback)
            {
                try { UpdateFallbackMenu(); }
                catch(Exception ex) { Fail(ex,"VR menu recovery stopped"); }
                return;
            }
            if (!failed)
            {
                try
                {
                    WorldAvailable = MySession.Static != null && Sandbox.Game.Gui.MyGuiScreenGamePlay.Static?.LoadingDone == true;
                    MenuOpen = MySession.Static == null || VRGUIManager.IsAnyDialogOpen() || Player.MenuKeyboard.Standalone;
                    bool chord = (MyInput.Static.IsKeyPress(MyKeys.LeftControl) || MyInput.Static.IsKeyPress(MyKeys.RightControl)) &&
                        (MyInput.Static.IsKeyPress(MyKeys.LeftAlt) || MyInput.Static.IsKeyPress(MyKeys.RightAlt));
                    if (chord && MyInput.Static.IsNewKeyPressed(MyKeys.R)) Player.Player.Headset.RequestRecenter();
                    if (chord && MyInput.Static.IsNewKeyPressed(MyKeys.D)) ShowDesktopPanel = !ShowDesktopPanel;
                    if (chord && MyInput.Static.IsNewKeyPressed(MyKeys.V)) OpenConfigDialog();
                    CustomUpdate();
                    recoveryAttempts=RecoveryAttempts(recoveryAttempts,recoveredAt,DateTime.UtcNow);
                }
                catch (Exception ex)
                {
                    FailToMenu(ex, "Update failed");
                }
            }
        }

        private static void UpdateFallbackMenu()
        {
            var session=MySession.Static;
            bool dialog=VRGUIManager.IsAnyDialogOpen() || Player.MenuKeyboard.Standalone;
            fallbackSawDialog |= dialog;
            bool changed=!ReferenceEquals(session,failedSession) || !ReferenceEquals(session?.LocalCharacter,failedCharacter) ||
                !ReferenceEquals(session?.ControlledEntity,failedControl) || fallbackSawDialog;
            if(CanResumeMenu(session!=null && Sandbox.Game.Gui.MyGuiScreenGamePlay.Static?.LoadingDone==true,
                session?.LocalCharacter?.IsDead==false,session?.ControlledEntity?.Entity!=null,dialog,changed,recoveryAttempts))
            {
                ResetWorldState();
                recoveryAttempts++; recoveredAt=DateTime.UtcNow;
                failedSession=failedCharacter=failedControl=null;
                menuFallback=false; failed=false; WorldAvailable=true; MenuOpen=false;
                Logger.Info("VR gameplay resumed after menu recovery");
                return;
            }
            WorldAvailable=false; MenuOpen=true;
            Player.Player.MainUpdate();
            Player.InputRouter.Update();
            Player.Controls.Static.Poll(Player.InputMode.Menu);
            Player.FloatingMenu.Update();
            Player.FloatingKeyboard.Update();
            Player.MenuPointer.Update();
        }

        // Any failure returns to the fallback menu, so time since the last resume is healthy play; after that long, earlier recoveries no longer count.
        internal static readonly TimeSpan RecoveryForgiven=TimeSpan.FromMinutes(5);
        internal static int RecoveryAttempts(int attempts,DateTime recovered,DateTime now) => attempts>0 && now-recovered>=RecoveryForgiven ? 0:attempts;
        internal static bool CanResumeMenu(bool worldReady,bool characterAlive,bool controlled,bool dialog,bool changed,int attempts) =>
            worldReady && characterAlive && controlled && !dialog && changed && attempts<2;

        private bool Initialize()
        {
            if (!OpenVR.IsRuntimeInstalled())
            {
                MyLog.Default.WriteLine("SpaceEngineersVR: OpenVR not found!");
                return false;
            }

            if (!OpenVR.IsHmdPresent())
            {
                MyLog.Default.WriteLine("SpaceEngineersVR: No VR headset found, please plug one in and reboot the game to play");
                return false;
            }

            Logger.Info("Starting Steam OpenVR");
            EVRInitError error = EVRInitError.None;
            OpenVR.Init(ref error, EVRApplicationType.VRApplication_Scene);
            Logger.Error($"Booting error = {error}");

            if (error != EVRInitError.None)
            {
                Logger.Critical("Failed to connect to SteamVR!");
                return false;
            }
            openVrInitialized = true;
            OpenVR.Compositor.SetTrackingSpace(ETrackingUniverseOrigin.TrackingUniverseStanding);
            // Set the manifest and acquire action handles before tracked devices or renderer callbacks exist.
            var controls = Player.Controls.Static;
            var headset = Player.Player.Headset;

            // Vanilla sends the game name and acronym to servers and Keen services; only the icon and splash change.
            MyPerGameSettings.GameIcon = Common.IconIcoPath;
            MyPerGameSettings.BasicGameInfo.SplashScreenImage = Common.IconPngPath;

            Logger.Info("Patching game");
            try
            {
                Harmony = new Harmony(Common.Name);
                Harmony.PatchAll(Assembly.GetExecutingAssembly());

                Patches.FrameInjections.Install(Harmony);
                Player.StereoFoliage.Warm();
                Multiplayer.MultiplayerRuntime.Capture=seq=>VrActive ? Player.TrackedArms.CapturePose(seq):null;
                Multiplayer.MultiplayerRuntime.Notify=message=> { if(VrActive) Player.EssentialHud.Notify(message); };
                Multiplayer.MultiplayerRuntime.ControlCount=seat=>Player.CockpitLayout.Count(seat.BlockDefinition.Id.SubtypeName);
                Multiplayer.MultiplayerRuntime.LoadLocal=Player.CockpitActions.LoadLocal;
                Multiplayer.MultiplayerRuntime.SaveLocal=Player.CockpitActions.SaveLocal;
                Multiplayer.MultiplayerRuntime.Enabled=true;
                Logger.Info("Stereo renderer, motion tools and menu overlay hooks installed.");

                MySession.AfterLoading += AfterLoadedWorld;
                MySession.OnUnloading += UnloadingWorld;

                DesktopResolution = MyRender11.Resolution;
            }
            catch
            {
                // Transpilers throw when the game's IL changes; leave the game unpatched rather than partly patched.
                Rollback();
                throw;
            }

            Logger.Info("Finalizing...");
            return true;
        }

        private void Rollback()
        {
            try
            {
                MySession.AfterLoading -= AfterLoadedWorld;
                MySession.OnUnloading -= UnloadingWorld;
                Multiplayer.MultiplayerRuntime.Enabled=false;
                Harmony?.UnpatchAll(Common.Name);
                Harmony = null;
                if (openVrInitialized) { OpenVR.Shutdown(); openVrInitialized = false; }
                MyLog.Default.WriteLine("SpaceEngineersVR: start failed; game patches and OpenVR were removed");
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLine("SpaceEngineersVR: rollback failed: " + ex);
            }
        }

        private void CustomUpdate()
        {
            Player.Player.MainUpdate();
            GUI.FirstRunSetup.Offer();
            Player.RemoteView.UpdateContext();
            Player.SpectatorView.UpdateContext();
            Player.Handedness.Update();
            Player.InputRouter.Update();
            Player.SeatRecenter.Update();
            Player.Controls.Static.Poll(Player.InputRouter.Mode);
            Player.HelmetHud.Update();
            Player.NativeGloves.Update();
            Player.SeatFit.Update();
            if(MenuOpen && MySession.Static?.LocalCharacter!=null)
                Player.TrackedArms.Update(MySession.Static.LocalCharacter);
            Player.FloatingKeyboard.Update();
            Player.FloatingMenu.Update();
            Player.RemoteView.Update();
            Player.DesktopWindow.Update();
            foreach(var integration in Integrations) integration.Refresh();
            Player.CockpitTouch.BeginFrame();
            Player.CockpitButtons.Update();
            Player.SpatialUi.Update();
            touchScreens.Update();
            arthurLcd.Update();
            Player.HandInteraction.UpdateTouch();
            Player.ThirdPersonView.Update();
            Player.SpectatorView.UpdateGestures();
            Player.HandGrab.Update();
            Player.BlockInspection.Update();
            Player.DampenerTargeting.Update();
            Player.GridSelection.Update();
            Player.ToolbarWheel.Update();
            Player.GameActions.RunScheduled();
            Player.CockpitControls.Update();
            Player.PlacementControls.Update();
            Player.CameraRig.Publish();
            Player.ViewGuard.Update();
            Player.WeaponHandling.Update();
            if(Player.WeaponHandling.ToolEquipped) Patches.MotionToolPatch.Refresh(MySession.Static.LocalCharacter);
            Player.NativeActions.Update();
            Player.BodyProximity.Update();
            Player.BootsHaptics.Update();
            if(Player.SeatFit.Eligible(Player.SeatFit.Seat))
                Player.TrackedArms.Update(Player.SeatFit.Seat.Pilot);
            if (Player.Controls.Static.Options.HasPressed) OpenConfigDialog();
            Player.MenuPointer.Update();

            if (MySession.Static?.LocalCharacter != null &&
                !MySession.Static.LocalCharacter.Components.Contains(typeof(VRMovementComponent)))
            {
                MySession.Static.LocalCharacter.Components.Add(new VRMovementComponent());
            }
            if (Player.InputRouter.Gameplay) { if(!Player.WeaponHandling.ToolEquipped) Player.HandInteraction.Update(); Player.CockpitControls.Draw(); }
            touchScreens.Draw();
            arthurLcd.Draw();
            Player.EssentialHud.Update();
            Player.ScreenEffects.Update();
            Player.HelmetLight.Publish();
            Player.SpatialUi.Publish();
        }

        // ReSharper disable once UnusedMember.Global
        public void OpenConfigDialog()
        {
            foreach (var screen in MyScreenManager.Screens) if (screen is MyPluginConfigDialog) return;
            MyGuiSandbox.AddScreen(new MyPluginConfigDialog());
            MenuOpen=true;
        }

        private static void ResetWorldState()
        {
            Multiplayer.MultiplayerRuntime.Reset();
            Player.GameActions.Reset();
            Player.SpectatorView.Reset(forgetTracked:true);
            Player.PlanetPreview.Begin();
            Player.RemoteView.Reset();
            Player.DesktopWindow.Reset();
            Player.BlockInspection.Reset();
            Player.InputRouter.RadialOpen = false;
            Player.ToolbarWheel.Close(resume:false);
            Player.EssentialHud.Reset();
            Player.BuildOrientationHud.Reset();
            Player.WorldMarkers.Reset();
            Player.InputRouter.Reset();
            Player.BodyProximity.Reset();
            Player.TrackedArms.Reset();
            Player.CockpitControls.Reset();
            Player.SeatFit.Reset();
            Player.SpatialUi.Reset();
            Player.NativeGloves.Reset();
            foreach(var integration in Integrations) integration.Reset();
            Player.CockpitButtons.Reset();
            Player.CameraRig.Reset(forgetHeight:true);
            Player.ThirdPersonView.Reset();
            Player.ViewFade.Set(Player.ViewFade.Source.Wall,0,.1f);
            Player.SeatRecenter.Reset();
        }

        public void AfterLoadedWorld()
        {
            recoveryAttempts=0;
            ResetWorldState();
            Logger.Info("Loading SE game");
            Player.Player.Headset.RequestRecenter();
            Player.Player.Headset.CreatePopup("Loaded Game");
        }

        public void UnloadingWorld()
        {
            recoveryAttempts=0;
            WorldAvailable = false;
            MenuOpen = true;
            Player.MenuKeyboard.Close();
            Player.Player.Headset.RequestRecenter();
            ResetWorldState();
            Logger.Info("Unloading SE game");
            Player.Player.Headset.CreatePopup("Unloaded Game");
        }
    }
}
