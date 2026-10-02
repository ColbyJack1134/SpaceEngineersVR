using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SpaceEngineersVR.Config
{
    public class SeatFitSetting
    {
        public string Subtype { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
    }
    public class PluginConfig : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private void SetValue<T>(ref T field, T value, [CallerMemberName] string propName = "")
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return;

            field = value;

            OnPropertyChanged(propName);
        }

        private void OnPropertyChanged([CallerMemberName] string propName = "")
        {
            PropertyChangedEventHandler propertyChanged = PropertyChanged;
            if (propertyChanged == null)
                return;

            propertyChanged(this, new PropertyChangedEventArgs(propName));
        }

        private bool enableKeyboardAndMouseControls = true;
        private bool enableCharacterRendering = true;

        private bool useHeadRotationForCharacter = true;
        private bool roomscaleMovement = true;
        private bool trackedArms = true;
        private bool adaptiveArms = true;
        public bool AdaptiveArms { get => adaptiveArms; set => SetValue(ref adaptiveArms,value); }
        private bool legacyShipTilt,physicalShipControlsOnly;
        public bool PhysicalShipControlsOnly { get => physicalShipControlsOnly; set => SetValue(ref physicalShipControlsOnly,value); }
        private bool developerTools;
        public bool DeveloperTools { get => developerTools; set => SetValue(ref developerTools,value); }
        private bool stableShadows=true,distantFlares=true,mirrorDesktop=true;
        public bool StableShadows { get => stableShadows; set => SetValue(ref stableShadows,value); }
        public bool DistantFlares { get => distantFlares; set => SetValue(ref distantFlares,value); }
        public bool MirrorDesktop { get => mirrorDesktop; set => SetValue(ref mirrorDesktop,value); }
        private float eyeRenderScale=1;
        public float EyeRenderScale { get => eyeRenderScale; set => SetValue(ref eyeRenderScale,Bound(value,.5f,1.5f,1)); }
        private bool invertShipPitch,invertJetpackPitch,hudWithVisorOpen;
        private bool? showVitals;
        private int waypointMode=-1;
        public bool InvertShipPitch { get => invertShipPitch; set => SetValue(ref invertShipPitch,value); }
        public bool InvertJetpackPitch { get => invertJetpackPitch; set => SetValue(ref invertJetpackPitch,value); }
        public bool HudWithVisorOpen { get => hudWithVisorOpen; set => SetValue(ref hudWithVisorOpen,value); }
        public bool ShowVitals { get => showVitals ?? helmetHudMode>0; set => SetValue(ref showVitals,(bool?)value); }
        public int WaypointMode { get => waypointMode<0 ? Math.Max(0,helmetHudMode-1):waypointMode; set => SetValue(ref waypointMode,Math.Max(0,Math.Min(2,value))); }
        internal void CycleHud()
        {
            if(!ShowVitals) { ShowVitals=true; WaypointMode=0; }
            else if(WaypointMode<2) WaypointMode++;
            else { ShowVitals=false; WaypointMode=0; }
        }
        private float thirdPersonPanSensitivity=1;
        public float ThirdPersonPanSensitivity { get => thirdPersonPanSensitivity; set => SetValue(ref thirdPersonPanSensitivity,Bound(value,.25f,2,1)); }
        private float thirdPersonZoomSensitivity=1;
        public float ThirdPersonZoomSensitivity { get => thirdPersonZoomSensitivity; set => SetValue(ref thirdPersonZoomSensitivity,Bound(value,.25f,2,1)); }
        private float thirdPersonRotationSensitivity=1;
        public float ThirdPersonRotationSensitivity { get => thirdPersonRotationSensitivity; set => SetValue(ref thirdPersonRotationSensitivity,Bound(value,.25f,2,1)); }
        private float thirdPersonPanGlide=1;
        public float ThirdPersonPanGlide { get => thirdPersonPanGlide; set => SetValue(ref thirdPersonPanGlide,Bound(value,0,2,1)); }
        private float thirdPersonZoomGlide=1;
        public float ThirdPersonZoomGlide { get => thirdPersonZoomGlide; set => SetValue(ref thirdPersonZoomGlide,Bound(value,0,2,1)); }
        private float thirdPersonRotationGlide=1;
        public float ThirdPersonRotationGlide { get => thirdPersonRotationGlide; set => SetValue(ref thirdPersonRotationGlide,Bound(value,0,2,1)); }
        private int thirdPersonMode;
        public int ThirdPersonMode { get => thirdPersonMode; set => SetValue(ref thirdPersonMode,value>=0 && value<=2 ? value : 0); }
        private AnchorOffsetSetting[] anchorOffsets=new AnchorOffsetSetting[0];
        public AnchorOffsetSetting[] AnchorOffsets { get => anchorOffsets; set => SetValue(ref anchorOffsets,value ?? new AnchorOffsetSetting[0]); }
        public bool LegacyShipTilt { get => legacyShipTilt; set => SetValue(ref legacyShipTilt, value); }
        public const float DefaultJetpackRollSensitivity = 0.25f, DefaultShipRollSensitivity = 0.6f;
        public const float MinRollSensitivity = 0.05f, MaxRollSensitivity = 2f;
        private float jetpackRollSensitivity = DefaultJetpackRollSensitivity;
        private float shipRollSensitivity = DefaultShipRollSensitivity;
        public float JetpackRollSensitivity
        {
            get => jetpackRollSensitivity;
            set => SetValue(ref jetpackRollSensitivity, LimitSensitivity(value, DefaultJetpackRollSensitivity));
        }
        public float ShipRollSensitivity
        {
            get => shipRollSensitivity;
            set => SetValue(ref shipRollSensitivity, LimitSensitivity(value, DefaultShipRollSensitivity));
        }
        private static float LimitSensitivity(float value, float fallback) => float.IsNaN(value) || float.IsInfinity(value)
            ? fallback : Math.Max(MinRollSensitivity, Math.Min(MaxRollSensitivity, value));
        private bool fighterCockpitSticks=true;
        private int helmetHudMode=2;
        private SeatFitSetting[] seatFits=new SeatFitSetting[0];
        private StickPlacementSetting[] stickPlacements=new StickPlacementSetting[0];
        private CockpitActionSetting[] cockpitActions=new CockpitActionSetting[0];
        public CockpitActionSetting[] CockpitActions { get => cockpitActions; set => SetValue(ref cockpitActions,value ?? new CockpitActionSetting[0]); }
        private MenuWindowSetting[] menuWindows=new MenuWindowSetting[0];
        public MenuWindowSetting[] MenuWindows { get => menuWindows; set => SetValue(ref menuWindows,value ?? new MenuWindowSetting[0]); }
        public StickPlacementSetting[] StickPlacements { get => stickPlacements; set => SetValue(ref stickPlacements,value ?? new StickPlacementSetting[0]); }
        public int HelmetHudMode { get => helmetHudMode; set => SetValue(ref helmetHudMode,Math.Max(0,Math.Min(3,value))); }
        public SeatFitSetting[] SeatFits { get => seatFits; set => SetValue(ref seatFits,value ?? new SeatFitSetting[0]); }
        private float physicalStickSensitivity=1f, physicalStickDeadzone=0.12f;
        public bool FighterCockpitSticks { get => fighterCockpitSticks; set => SetValue(ref fighterCockpitSticks,value); }
        public float PhysicalStickSensitivity
        {
            get => physicalStickSensitivity;
            set => SetValue(ref physicalStickSensitivity,Bound(value,0.25f,2f,1f));
        }
        public float PhysicalStickDeadzone
        {
            get => physicalStickDeadzone;
            set => SetValue(ref physicalStickDeadzone,Bound(value,0.02f,0.35f,0.12f));
        }
        private static float Bound(float value,float min,float max,float fallback) => float.IsNaN(value) || float.IsInfinity(value)
            ? fallback : Math.Max(min,Math.Min(max,value));
        public bool TrackedArms { get => trackedArms; set => SetValue(ref trackedArms,value); }
        private bool controllerRelativeMovement;
        private bool controllerMenuPointer = true;

        public bool RoomscaleMovement { get => roomscaleMovement; set => SetValue(ref roomscaleMovement, value); }
        public bool ControllerRelativeMovement { get => controllerRelativeMovement; set => SetValue(ref controllerRelativeMovement, value); }
        public bool ControllerMenuPointer { get => controllerMenuPointer; set => SetValue(ref controllerMenuPointer, value); }


        private float playerHeight = 1.69f;
        private float playerArmSpan = 1.66f;

        public bool EnableKeyboardAndMouseControls
        {
            get => enableKeyboardAndMouseControls;
            set => SetValue(ref enableKeyboardAndMouseControls, value);
        }

        public bool EnableCharacterRendering
        {
            get => enableCharacterRendering;
            set => SetValue(ref enableCharacterRendering, value);
        }

        public bool UseHeadRotationForCharacter
        {
            get => useHeadRotationForCharacter;
            set => SetValue(ref useHeadRotationForCharacter, value);
        }

        public float PlayerHeight
        {
            get => playerHeight;
            set => SetValue(ref playerHeight, value);
        }
        public float PlayerArmSpan
        {
            get => playerArmSpan;
            set => SetValue(ref playerArmSpan, value);
        }
    }
}
