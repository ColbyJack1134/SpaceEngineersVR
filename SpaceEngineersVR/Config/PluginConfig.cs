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
    public partial class PluginConfig : INotifyPropertyChanged
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
            CaptureActiveHudProfile(propName);
            PropertyChangedEventHandler propertyChanged = PropertyChanged;
            if (propertyChanged == null)
                return;

            propertyChanged(this, new PropertyChangedEventArgs(propName));
        }

        private string[] recentSearchActions=Array.Empty<string>();
        public string[] RecentSearchActions { get => recentSearchActions; set => SetValue(ref recentSearchActions,value ?? Array.Empty<string>()); }

        private bool enableKeyboardAndMouseControls = true;
        private bool enableCharacterRendering = true;

        private bool useHeadRotationForCharacter = true;
        private bool roomscaleMovement = true;
        private bool trackedArms = true;
        private bool adaptiveArms = true;
        public bool AdaptiveArms { get => adaptiveArms; set => SetValue(ref adaptiveArms,value); }
        private bool legacyShipTilt,physicalShipControlsOnly,holdGripRotation;
        public bool PhysicalShipControlsOnly { get => physicalShipControlsOnly; set => SetValue(ref physicalShipControlsOnly,value); }
        private bool bodyCalibrated,seatedPlay,fitBodyOnFoot,setupCompleted;
        public bool SetupCompleted { get => setupCompleted; set => SetValue(ref setupCompleted,value); }
        private float measuredEyeHeight;
        public bool FitBodyOnFoot { get => fitBodyOnFoot; set => SetValue(ref fitBodyOnFoot,value); }
        public float MeasuredEyeHeight { get => measuredEyeHeight; set => SetValue(ref measuredEyeHeight,Bound(value,0,2.5f,0)); }
        private float seatedReference=1.2f;
        public bool BodyCalibrated { get => bodyCalibrated; set => SetValue(ref bodyCalibrated,value); }
        public bool SeatedPlay { get => seatedPlay; set => SetValue(ref seatedPlay,value); }
        public float SeatedReference { get => seatedReference; set => SetValue(ref seatedReference,Bound(value,.3f,2.5f,1.2f)); }
        private bool bodyProximityFade=true;
        public bool BodyProximityFade { get => bodyProximityFade; set => SetValue(ref bodyProximityFade,value); }
        private bool onFootBodyProximityFade=true,hideFirstPersonBody;
        public bool OnFootBodyProximityFade { get => onFootBodyProximityFade; set => SetValue(ref onFootBodyProximityFade,value); }
        public bool HideFirstPersonBody { get => hideFirstPersonBody; set => SetValue(ref hideFirstPersonBody,value); }
        private float turretAimSensitivity=2,cameraZoomSensitivity=2;
        public float TurretAimSensitivity { get => turretAimSensitivity; set => SetValue(ref turretAimSensitivity,Bound(value,.25f,4,2)); }
        public float CameraZoomSensitivity { get => cameraZoomSensitivity; set => SetValue(ref cameraZoomSensitivity,Bound(value,.25f,4,2)); }
        private bool inspectWithoutGrip;
        public bool InspectWithoutGrip { get => inspectWithoutGrip; set => SetValue(ref inspectWithoutGrip,value); }
        private bool grabSnapToCenter;
        public bool GrabSnapToCenter { get => grabSnapToCenter; set => SetValue(ref grabSnapToCenter,value); }
        private bool doNotDisturb;
        public bool DoNotDisturb { get => doNotDisturb; set => SetValue(ref doNotDisturb,value); }
        private bool developerTools;
        public bool DeveloperTools { get => developerTools; set => SetValue(ref developerTools,value); }
        private bool programmableBlockData=true;
        public bool ProgrammableBlockData { get => programmableBlockData; set => SetValue(ref programmableBlockData,value); }
        private bool stableShadows=true,distantFlares=true,mirrorDesktop=true;
        public bool StableShadows { get => stableShadows; set => SetValue(ref stableShadows,value); }
        public bool DistantFlares { get => distantFlares; set => SetValue(ref distantFlares,value); }
        public bool MirrorDesktop { get => mirrorDesktop; set => SetValue(ref mirrorDesktop,value); }
        private bool hiddenAreaMask=true;
        public bool HiddenAreaMask { get => hiddenAreaMask; set => SetValue(ref hiddenAreaMask,value); }
        private float eyeRenderScale=1;
        public float EyeRenderScale { get => eyeRenderScale; set => SetValue(ref eyeRenderScale,Bound(value,.5f,1.5f,1)); }
        private bool dynamicResolution;
        public bool DynamicResolution { get => dynamicResolution; set => SetValue(ref dynamicResolution,value); }
        private float targetFrameRate=60;
        public float TargetFrameRate { get => targetFrameRate; set => SetValue(ref targetFrameRate,(float)Math.Round(Bound(value,30,144,60))); }
        private float remoteFeedScale=5f/6;
        public float RemoteFeedScale { get => remoteFeedScale; set => SetValue(ref remoteFeedScale,Bound(value,.5f,1.5f,5f/6)); }
        private float particleDensity=.5f;
        public float ParticleDensity { get => particleDensity; set => SetValue(ref particleDensity,Bound(value,.15f,1,.5f)); }
        private bool invertShipPitch,invertJetpackPitch,hudWithVisorOpen;
        private bool? showVitals;
        private int waypointMode=-1;
        public bool InvertShipPitch { get => invertShipPitch; set => SetValue(ref invertShipPitch,value); }
        public bool InvertJetpackPitch { get => invertJetpackPitch; set => SetValue(ref invertJetpackPitch,value); }
        private float turnSensitivity=1.5f;
        public float TurnSensitivity { get => turnSensitivity; set => SetValue(ref turnSensitivity,Bound(value,.25f,4,1.5f)); }
        private bool snapTurn;
        public bool SnapTurn { get => snapTurn; set => SetValue(ref snapTurn,value); }
        private float snapTurnAngle=30;
        public float SnapTurnAngle { get => snapTurnAngle; set => SetValue(ref snapTurnAngle,Bound(value,15,90,30)); }
        private bool stickJump;
        public bool StickJump { get => stickJump; set => SetValue(ref stickJump,value); }
        public bool HudWithVisorOpen { get => hudWithVisorOpen; set => SetValue(ref hudWithVisorOpen,value); }
        private bool reduceScreenEffects=true;
        public bool ReduceScreenEffects { get => reduceScreenEffects; set => SetValue(ref reduceScreenEffects,value); }
        private bool visorDust,hideDustWithVisorOpen=true;
        public bool VisorDust { get => visorDust; set => SetValue(ref visorDust,value); }
        public bool HideDustWithVisorOpen { get => hideDustWithVisorOpen; set => SetValue(ref hideDustWithVisorOpen,value); }
        public bool ShowVitals { get => showVitals ?? helmetHudMode>0; set => SetValue(ref showVitals,(bool?)value); }
        public int WaypointMode { get => waypointMode<0 ? Math.Max(0,helmetHudMode-1):waypointMode; set => SetValue(ref waypointMode,Math.Max(0,Math.Min(2,value))); }
        private bool signalEdges=true,signalRings=true,showGps=true,showContacts=true,showResources=true;
        private float ownSignalRange=1,friendlySignalRange=1,otherSignalRange=1;
        public float OwnSignalRange { get => ownSignalRange; set => SetValue(ref ownSignalRange,Bound(value,0,1,1)); }
        public float FriendlySignalRange { get => friendlySignalRange; set => SetValue(ref friendlySignalRange,Bound(value,0,1,1)); }
        public float OtherSignalRange { get => otherSignalRange; set => SetValue(ref otherSignalRange,Bound(value,0,1,1)); }
        public bool GroupSignals { get => true; set { } }
        private bool characterMarkerRoll=true;
        public bool CharacterMarkerRoll { get => characterMarkerRoll; set => SetValue(ref characterMarkerRoll,value); }
        private bool faceMarkersTowardViewer=true;
        public bool FaceMarkersTowardViewer { get => faceMarkersTowardViewer; set => SetValue(ref faceMarkersTowardViewer,value); }
        public bool SignalEdges { get => signalEdges; set => SetValue(ref signalEdges,value); }
        public bool SignalRings { get => signalRings; set => SetValue(ref signalRings,value); }
        private bool shipCrosshair=true;
        public bool ShipCrosshair { get => shipCrosshair; set => SetValue(ref shipCrosshair,value); }
        public const float DefaultCrosshairScale=1/3f;
        private float crosshairScale=DefaultCrosshairScale;
        // Fraction of the flat game's crosshair size; ship crosshair, its readouts and the on-foot hit marker.
        public float CrosshairScale { get => crosshairScale; set => SetValue(ref crosshairScale,Bound(value,.2f,1,DefaultCrosshairScale)); }
        public bool ShowGps { get => showGps; set => SetValue(ref showGps,value); }
        public bool ShowContacts { get => showContacts; set => SetValue(ref showContacts,value); }
        public bool ShowResources { get => showResources; set => SetValue(ref showResources,value); }
        internal void CycleHud()
        {
            InitializeHudProfiles();
            SelectHudProfile((hudProfileIndex+1)%hudProfiles.Length);
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
        public bool HoldGripRotation { get => holdGripRotation; set => SetValue(ref holdGripRotation, value); }
        private bool clearTerrainToggle;
        public bool ClearTerrainToggle { get => clearTerrainToggle; set => SetValue(ref clearTerrainToggle,value); }
        private bool doubleTapToolLock=true;
        public bool DoubleTapToolLock { get => doubleTapToolLock; set => SetValue(ref doubleTapToolLock,value); }
        private bool lockBlockWhileRotating=true;
        public bool LockBlockWhileRotating { get => lockBlockWhileRotating; set => SetValue(ref lockBlockWhileRotating,value); }
        private bool leftHanded;
        public bool LeftHanded { get => leftHanded; set => SetValue(ref leftHanded,value); }
        public const float DefaultJetpackRoll = 0.15f, DefaultShipRollSensitivity = 0.6f;
        public const float MinRollSensitivity = 0.05f, MaxRollSensitivity = 2f;
        // The jetpack slider shows the default as 50%, so its range is 10-200% of that scale.
        public const float JetpackRollPercent = 50/DefaultJetpackRoll, MinJetpackRoll = 10/JetpackRollPercent, MaxJetpackRoll = 200/JetpackRollPercent;
        private float jetpackRoll = DefaultJetpackRoll;
        private float shipRollSensitivity = DefaultShipRollSensitivity;
        // Renamed from JetpackRollSensitivity so saved values from the old scale reset to the new default.
        public float JetpackRoll
        {
            get => jetpackRoll;
            set => SetValue(ref jetpackRoll, float.IsNaN(value) || float.IsInfinity(value) ? DefaultJetpackRoll : Math.Max(MinJetpackRoll, Math.Min(MaxJetpackRoll, value)));
        }
        public float ShipRollSensitivity
        {
            get => shipRollSensitivity;
            set => SetValue(ref shipRollSensitivity, LimitSensitivity(value, DefaultShipRollSensitivity));
        }
        private static float LimitSensitivity(float value, float fallback) => float.IsNaN(value) || float.IsInfinity(value)
            ? fallback : Math.Max(MinRollSensitivity, Math.Min(MaxRollSensitivity, value));
        private bool fighterCockpitSticks=true, stickTwist=true;
        private int helmetHudMode=2;
        private SeatFitSetting[] seatFits=new SeatFitSetting[0];
        private StickPlacementSetting[] stickPlacements=new StickPlacementSetting[0];
        private CockpitActionSetting[] cockpitActions=new CockpitActionSetting[0];
        public CockpitActionSetting[] CockpitActions { get => cockpitActions; set => SetValue(ref cockpitActions,value ?? new CockpitActionSetting[0]); }
        private QuickToolbarSetting[] quickToolbars=new QuickToolbarSetting[0];
        public QuickToolbarSetting[] QuickToolbars { get => quickToolbars; set => SetValue(ref quickToolbars,value ?? new QuickToolbarSetting[0]); }
        private CockpitStateSetting[] cockpitStates=new CockpitStateSetting[0];
        public CockpitStateSetting[] CockpitStates { get => cockpitStates; set => SetValue(ref cockpitStates,value ?? new CockpitStateSetting[0]); }
        private MenuWindowSetting[] menuWindows=new MenuWindowSetting[0];
        public MenuWindowSetting[] MenuWindows { get => menuWindows; set => SetValue(ref menuWindows,value ?? new MenuWindowSetting[0]); }
        public StickPlacementSetting[] StickPlacements { get => stickPlacements; set => SetValue(ref stickPlacements,value ?? new StickPlacementSetting[0]); }
        public int HelmetHudMode { get => helmetHudMode; set => SetValue(ref helmetHudMode,Math.Max(0,Math.Min(3,value))); }
        public SeatFitSetting[] SeatFits { get => seatFits; set => SetValue(ref seatFits,value ?? new SeatFitSetting[0]); }
        private float physicalStickSensitivity=1f, physicalStickDeadzone=0.08f;
        public bool FighterCockpitSticks { get => fighterCockpitSticks; set => SetValue(ref fighterCockpitSticks,value); }
        public bool StickTwist { get => stickTwist; set => SetValue(ref stickTwist,value); }
        public float PhysicalStickSensitivity
        {
            get => physicalStickSensitivity;
            set => SetValue(ref physicalStickSensitivity,Bound(value,0.25f,2f,1f));
        }
        public float PhysicalStickDeadzone
        {
            get => physicalStickDeadzone;
            set => SetValue(ref physicalStickDeadzone,Bound(value,0.02f,0.35f,0.08f));
        }
        private float physicalStickExponent=2f,physicalStickSmoothing=.025f;
        public float PhysicalStickExponent
        {
            get => physicalStickExponent;
            set => SetValue(ref physicalStickExponent,Bound(value,1f,3f,2f));
        }
        public float PhysicalStickSmoothing
        {
            get => physicalStickSmoothing;
            set => SetValue(ref physicalStickSmoothing,Bound(value,0f,.06f,.025f));
        }
        private static float Bound(float value,float min,float max,float fallback) => float.IsNaN(value) || float.IsInfinity(value)
            ? fallback : Math.Max(min,Math.Min(max,value));
        public bool TrackedArms { get => trackedArms; set => SetValue(ref trackedArms,value); }
        private bool tapHoldSticks,tapHoldLevers;
        public bool TapHoldSticks { get => tapHoldSticks; set => SetValue(ref tapHoldSticks,value); }
        public bool TapHoldLevers { get => tapHoldLevers; set => SetValue(ref tapHoldLevers,value); }
        private float hapticStrength=1;
        public float HapticStrength { get => hapticStrength; set => SetValue(ref hapticStrength,Bound(value,0,1,1)); }
        private bool keepScreenPlacement;
        public bool KeepScreenPlacement { get => keepScreenPlacement; set => SetValue(ref keepScreenPlacement,value); }
        private string desktopMonitor;
        public string DesktopMonitor { get => desktopMonitor; set => SetValue(ref desktopMonitor,value); }
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
