using System.Collections.Generic;
using Sandbox.Graphics.GUI;

namespace SpaceEngineersVR.GUI
{
    // Hover text for settings, keyed by the label shown beside the control.
    internal static class SettingsTooltips
    {
        private static readonly Dictionary<string,string> text=new Dictionary<string,string>
        {
            ["Seated play"]="Standing eye height while seated",
            ["Seated play (toggle for standing)"]="Standing eye height while seated",
            ["Controller-relative movement"]="Walk where the left controller points",
            ["Haptics strength"]="Scales all vibration. 0% is off",
            ["Recenter"]="Resets the view to your head",
            ["Physical cockpit sticks"]="Grab the cockpit joysticks",
            ["Enable physical cockpit sticks"]="Grab the cockpit joysticks",
            ["Tap to hold joysticks"]="Tap grip to latch a stick",
            ["Tap to hold levers"]="Tap grip to latch a lever",
            ["Tap to hold analog levers"]="Tap grip to latch a lever",
            ["Keep floating screens where left"]="Off: screens out of view reopen in front",
            ["HUD state"]="Select the state edits save to",
            ["Vitals"]="Suit and ship gauges",
            ["Markers"]="None, icons only, or with names",
            ["Headset resolution"]="Per-eye render scale",
            ["Camera feed resolution"]="Camera and turret view detail",
            ["Particle density"]="Share of particles drawn",
            ["Desktop mirror"]="Show the left eye on the monitor",
            ["Left-handed"]="Swap hands on foot and building",
            ["Body follows head turns"]="Body turns to face your view",
            ["Roomscale body movement"]="Walking in your room moves you",
            ["Hold grip to rotate blocks"]="Off: tap grip to toggle rotate",
            ["Lock block position while rotating in build mode"]="Preview stays put while rotating",
            ["Snap turning"]="Turn in fixed steps",
            ["Smooth turn speed"]="Stick turn rate",
            ["Proximity fade on foot"]="Fade the body your head enters",
            ["Proximity fade in seats"]="Fade the body you lean into",
            ["Hide first-person body"]="Hands stay visible",
            ["Controller menu pointer"]="Laser pointer in menus",
            ["Keyboard / mouse gameplay"]="Keyboard and mouse alongside controllers",
            ["Tracked arms"]="Arms follow your controllers",
            ["Legacy hand-tilt ship steering"]="Tilt your hand to steer ships",
            ["Double-tap to keep tools running"]="Double-tap trigger to lock tools on",
            ["Right stick up jumps"]="Stick up jumps, or stands from crouch",
            ["VR data to [SEVR] programmable blocks"]="Head and hand data for [SEVR] blocks",
            ["Turret aim sensitivity"]="Stick aim speed in turrets",
            ["Camera zoom sensitivity"]="Zoom speed in cameras and turrets",
            ["Pan sensitivity"]="View movement per hand drag",
            ["Pan glide"]="Coast after releasing a pan",
            ["Zoom glide"]="Coast after releasing a zoom",
            ["Rotation glide"]="Coast after releasing a turn",
            ["Own contacts"]="Max distance for your own contacts",
            ["Friendly / unowned"]="Max distance for friendly and unowned",
            ["Neutral / hostile"]="Max distance for neutral and hostile",
            ["Character marker roll"]="Markers stay level when you tilt",
            ["Face markers toward viewer"]="Markers turn to face you",
            ["Direction arrows"]="Arrows to off-screen markers",
            ["Targeting rings"]="Target rings and lead marks",
            ["Contacts"]="Antenna and beacon markers",
            ["Warning pulse"]="Vibrate on weapon and shield warnings",
            ["Repeat pulse every"]="Repeat while a warning lasts",
            ["Show HUD with visor open"]="HUD stays on with the visor open",
            ["Ship crosshair"]="Needs vitals or markers on",
            ["Block info without grip"]="Block info by pointing alone",
            ["Crosshair size"]="Crosshair and hit mark size",
            ["Reduce screen effects"]="No chromatic aberration or vignette",
            ["Visor dust"]="Dirt specks on the visor",
            ["Hide dust with visor open"]="Dust fades as the visor opens",
            ["Standing height (cm)"]="Sets body size and eye level",
            ["Measure headset height"]="Stand upright, then press",
            ["Set seated head position"]="Current head height becomes seated",
            ["Fit body on foot"]="Scale the body to your height",
            ["Jetpack roll"]="Jetpack stick roll rate",
            ["Ship roll"]="Ship roll rate, sticks included",
            ["Physical ship sticks only (first person)"]="Thumbsticks stop flying ships in first person",
            ["Twist sticks for yaw and vertical"]="Twist right stick to yaw, left to climb",
            ["Notifications"]="Game notices on the HUD",
            ["Distances"]="Distance under markers",
            ["Hit markers on foot"]="Shows where your shots hit",
            ["Add state"]="Add or remove a fifth state",
            ["Dynamic resolution"]="Lowers resolution to hold frame rate",
            ["Target frame rate"]="Frame rate to hold",
            ["Performance panel"]="Frame timing overlay, not saved",
        };

        internal static void Set(string label,params MyGuiControlBase[] controls)
        {
            if(!text.TryGetValue(label,out string tip)) return;
            foreach(var control in controls) control.SetToolTip(tip);
        }
    }
}
