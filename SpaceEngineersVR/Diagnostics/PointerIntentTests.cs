using System;
using SpaceEngineersVR.Player.Control;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class PointerIntentTests
    {
        public static void Run(Action<string> log)
        {
            MainMenuStartup(log);
            var intent=new PointerIntent();
            intent.Begin(true,1,true);
            Require(!intent.Capture("panel",true),"Held trigger acquired on entry");
            intent.Begin(true,0,false);
            intent.Begin(true,.1f,false);
            Require(!intent.Capture("panel",false),"Miss acquired panel");
            Require(intent.Preview,"Light squeeze needs a target before displaying ray");
            intent.Begin(true,.8f,true);
            Require(!intent.Capture("panel",false),"Miss acquired panel at full squeeze");
            intent.Begin(true,.8f,true);
            Require(!intent.Capture("panel",true),"Firing became a UI click");
            intent.Begin(true,.1f,false);
            Require(!intent.Preview && !intent.Capture("panel",true),"Easing a firing trigger reopened interaction");
            intent.Begin(true,0,false);
            intent.Begin(true,.1f,false);
            Require(!intent.Capture("panel",false),"Preview miss captured UI");
            intent.Begin(true,.1f,false);
            Require(intent.Capture("panel",true),"Light squeeze did not acquire");
            intent.Begin(true,.8f,true);
            Require(intent.Capture("panel",false) && intent.Owned,"Leaving panel resumed firing");
            Require(!intent.Capture("other",true),"Held squeeze switched panels");
            intent.Begin(false,.8f,true);
            intent.Begin(true,.8f,true);
            Require(!intent.Capture("panel",true) && !intent.Preview,"Tracking return acquired held trigger");
            intent.Begin(true,0,false);
            intent.Begin(true,1,true);
            Require(intent.Capture("panel",true),"Fresh direct click lost");
            intent.Begin(true,.02f,false);
            Require(!intent.Owned,"Release retained ownership");
            intent.Begin(true,0,false,false);
            intent.Begin(true,.1f,false,false);
            Require(!intent.Preview && !intent.Capture("panel",true),"Unpointed hand acquired a cockpit ray");
            intent.Begin(true,.1f,false,true);
            Require(intent.Preview && !intent.Capture("panel",false),"Pointing preview still requires a valid target");
            intent.Begin(true,.8f,true,false);
            intent.Begin(true,.8f,true,true);
            Require(!intent.Preview && !intent.Capture("panel",true),"Pointing stole an already firing squeeze");
            intent.Begin(true,0,false,true);
            intent.Begin(true,.1f,false,true); intent.Capture("panel",true);
            intent.Begin(true,.8f,true,false);
            Require(intent.Owned && !intent.Preview,"Leaving the pointing region released captured firing input");
            intent.Reset(); intent.Begin(true,0,false);
            intent.Begin(true,.8f,true);
            intent.Begin(true,.8f,true,true,"Seat");
            Require(!intent.Capture("Seat",true),"Nearby finger stole an already firing squeeze");
            intent.Begin(true,0,false);
            intent.Begin(true,.8f,true); intent.Capture("switch",true);
            intent.Begin(true,.8f,true,true,"Seat");
            Require(!intent.Capture("Seat",true) && intent.Capture("switch",false),"Near preference transferred an already clicked UI squeeze");
            Require(Player.InputRouter.AllowsTrackedItems(Player.InputMode.Radial,false),"Radial loses tracked tool pose");
            Require(!Player.InputRouter.AllowsTrackedItems(Player.InputMode.Blocked,false) &&
                !Player.InputRouter.AllowsTrackedItems(Player.InputMode.Radial,true),"Blocked/menu context retains gameplay tool pose");
            log("PASS cockpit pointer intent: light squeeze, firing across panels, capture through misses, release and tracking gates");
        }
        private static void MainMenuStartup(Action<string> log)
        {
            Require(Sandbox.Game.World.MySession.Static==null && Sandbox.Game.World.MySector.MainCamera==null,
                "Startup fixture requires no loaded world or camera");
            var failed=HarmonyLib.AccessTools.Field(typeof(Plugin.Main),"failed");
            bool saved=(bool)failed.GetValue(null);
            try
            {
                failed.SetValue(null,false);
                Player.CockpitTouch.BeginFrame();
                Player.CockpitTouch.BeginFrame();
                Require(!Player.CockpitTouch.LeftPointing && !Player.CockpitTouch.RightPointing && !Player.CockpitTouch.OwnsRight,
                    "Main menu retained cockpit pointing or trigger ownership");
            }
            finally { failed.SetValue(null,saved); }
            log("PASS cockpit main-menu startup: VR active, no session/camera, repeated input frames");
        }
        private static void Require(bool value,string message) { if(!value) throw new Exception(message); }
    }
}
