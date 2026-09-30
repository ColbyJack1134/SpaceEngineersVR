using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using System.Runtime.Serialization;
using HarmonyLib;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.Game.Screens.Helpers;
using SpaceEngineersVR.Player;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    // Run only after native initialization, with no loaded world. Proxies capture
    // native sprite output; the optional source-built mod fixture stays outside distribution.
    internal static class NativeIntegrationTests
    {
        private sealed class Item : MyToolbarItem
        {
            public int Count;
            public Item() { SetEnabled(true); WantsToBeActivated=true; }
            public override bool Activate() { Count++; return true; }
            public override bool Init(MyObjectBuilder_ToolbarItem data) => true;
            public override MyObjectBuilder_ToolbarItem GetObjectBuilder() => null;
            public override bool AllowedInToolbarType(MyToolbarType type) => true;
            public override ChangeInfo Update(MyEntity owner,long playerID=0,bool anyoneCanUse=false) => ChangeInfo.None;
        }
        private sealed class Proxy : RealProxy
        {
            public MatrixD World=MatrixD.Identity;
            public readonly List<MySprite> Sprites=new List<MySprite>();
            public Proxy(Type type) : base(type) { }
            public override IMessage Invoke(IMessage message)
            {
                var call=(IMethodCallMessage)message; object value=null;
                switch(call.MethodName)
                {
                    case "get_WorldMatrix": value=World; break;
                    case "get_TextureSize": value=new Vector2(512); break;
                    case "get_SurfaceSize": value=new Vector2(512,256); break;
                    case "get_ScriptBackgroundColor": value=Color.Black; break;
                    case "get_ScriptForegroundColor": value=Color.White; break;
                    case "DrawFrame": value=new MySpriteDrawFrame(f=> { Sprites.Clear(); f.AddToList(Sprites); }); break;
                    default:
                        var method=(MethodInfo)call.MethodBase;
                        if(method.ReturnType.IsValueType && method.ReturnType!=typeof(void)) value=Activator.CreateInstance(method.ReturnType);
                        break;
                }
                return new ReturnMessage(value,null,0,call.LogicalCallContext,call);
            }
        }
        private static void Require(bool condition,string reason) { if(!condition) throw new Exception(reason); }
        public static void Run(Action<string> log)
        {
            var character=(Sandbox.Game.Entities.Character.MyCharacter)FormatterServices.GetUninitializedObject(typeof(Sandbox.Game.Entities.Character.MyCharacter));
            var placer=FormatterServices.GetUninitializedObject(AccessTools.TypeByName("SpaceEngineers.Game.Entities.Weapons.MyCubePlacer"));
            AccessTools.Field(character.GetType(),"m_currentWeapon").SetValue(character,placer);
            Require(Alignment.ToolKey(character)==null,"Block placer incorrectly treated as a physical inventory tool");
            Require(Alignment.ToolKey(null)==null,"Missing character crashes tool calibration");
            log("PASS block-placement regression: native MyCubePlacer without a PhysicalObject bypasses tool calibration.");
            var toolbar=new MyToolbar(MyToolbarType.Ship,9,2); var first=new Item(); var second=new Item();
            toolbar.SetItemAtIndex(0,first); toolbar.SetItemAtIndex(9,second);
            toolbar.ActivateItemAtSlot(0,playActivationSound:false);
            toolbar.SwitchToPage(1); toolbar.ActivateItemAtSlot(0,playActivationSound:false);
            second.SetEnabled(false); toolbar.ActivateItemAtSlot(0,playActivationSound:false);
            Require(first.Count==1 && second.Count==1,"Native cockpit toolbar page/disabled activation failed");
            log("PASS native physical-button dispatch: installed MyToolbar selects the current page and refuses disabled items.");

            foreach(var toolbarType in new[] { MyToolbarType.Character,MyToolbarType.Ship })
            {
                var hotbar=new MyToolbar(toolbarType,9,2);
                var occupied=new Item(); hotbar.SetItemAtIndex(0,occupied);
                int assigned=-2,calls=0;
                Action<int> configure=slot=> { assigned=slot; calls++; };
                Require(ToolbarWheel.SelectSlot(hotbar,0,true,configure)==ToolbarWheel.SlotResult.Assigned && assigned==0 &&
                    occupied.Count==0 && ReferenceEquals(hotbar.GetItemAtSlot(0),occupied),"Assignment override activated or cleared an occupied hotbar slot");
                hotbar.SwitchToPage(1);
                Require(ToolbarWheel.SelectSlot(hotbar,0,false,configure)==ToolbarWheel.SlotResult.Assigned && assigned==0 && calls==2 &&
                    occupied.Count==0 && hotbar.CurrentPage==1,"Empty slot assignment used the wrong native page");
                Require(ToolbarWheel.SelectSlot(hotbar,-1,false,configure)==ToolbarWheel.SlotResult.Ignored && calls==2,
                    "Neutral wheel opened assignment");
                Require(ToolbarWheel.SelectSlot(hotbar,-1,true,configure)==ToolbarWheel.SlotResult.Assigned && assigned==-1 && calls==3,
                    "Explicit G shortcut lost neutral/other-category behavior");
                hotbar.SwitchToPage(0);
                Require(ToolbarWheel.SelectSlot(hotbar,0,false,configure)==ToolbarWheel.SlotResult.Activated && occupied.Count==1 && calls==3,
                    "Ordinary occupied slot stopped activating");
                occupied.SetEnabled(false);
                Require(ToolbarWheel.SelectSlot(hotbar,0,false,configure)==ToolbarWheel.SlotResult.Unavailable && occupied.Count==1 && calls==3,
                    "Disabled occupied slot activated or was treated as empty");
                Require(ToolbarWheel.SelectSlot(hotbar,0,true,configure)==ToolbarWheel.SlotResult.Assigned && occupied.Count==1 && calls==4,
                    "Disabled occupied slot could not be explicitly reassigned");
            }
            log("PASS radial hotbar assignment: native Character/Ship toolbars, current-page empty slots, occupied/disabled override without activation or removal, neutral cancel and ordinary activation.");

            var actions=new MyToolbar(MyToolbarType.ButtonPanel,4,1);
            var push=new Item(); var on=new Item(); var off=new Item();
            actions.SetItemAtIndex(0,push); actions.SetItemAtIndex(2,on); actions.SetItemAtIndex(3,off);
            for(int page=0;page<2;page++)
            {
                toolbar.SwitchToPage(page);
                Require(actions.ActivateItemAtIndex(0) && actions.ActivateItemAtIndex(2) && actions.ActivateItemAtIndex(3),"Independent physical action dispatch failed");
            }
            Require(push.Count==2 && on.Count==2 && off.Count==2 && first.Count==1 && second.Count==1,
                "Physical switch changed or invoked the ship hotbar");
            on.SetEnabled(false); Require(!actions.ActivateItemAtIndex(2) && on.Count==2,"Disabled switch action ran");
            var saved=new MyObjectBuilder_Toolbar { ToolbarType=MyToolbarType.ButtonPanel,Slots=new List<MyObjectBuilder_Toolbar.Slot> {
                new MyObjectBuilder_Toolbar.Slot { Index=2,Data=new MyObjectBuilder_ToolbarItemTerminalBlock {
                    BlockEntityId=987654321,_Action="Run",CustomIconTitle="switch app",Parameters=new List<MyObjectBuilder_ToolbarItemActionParameter> {
                        new MyObjectBuilder_ToolbarItemActionParameter { TypeCode=TypeCode.String,Value="test argument" } } } } } };
            var utilities=(VRage.Game.ModAPI.IMyUtilities)Sandbox.ModAPI.MyAPIUtilities.Static;
            string xml=utilities.SerializeToXML(saved);
            var loaded=utilities.SerializeFromXML<MyObjectBuilder_Toolbar>(xml);
            var restored=(MyObjectBuilder_ToolbarItemTerminalBlock)loaded.Slots[0].Data;
            Require(loaded.Slots[0].Index==2 && restored.BlockEntityId==987654321 && restored._Action=="Run" &&
                restored.Parameters[0].Value=="test argument","Native action serialization lost slot, target, action or PB argument");
            log("PASS independent physical controls: ButtonPanel actions ignore ship hotbar pages; separate I/O actions and disabled gating; native XML round trip preserves target/action/PB argument. No world modified.");

            var surfaceProxy=new Proxy(typeof(Sandbox.ModAPI.IMyTextSurface));
            var blockProxy=new Proxy(typeof(Sandbox.ModAPI.IMyTerminalBlock));
            var surface=(Sandbox.ModAPI.IMyTextSurface)surfaceProxy.GetTransparentProxy();
            var block=(Sandbox.ModAPI.IMyTerminalBlock)blockProxy.GetTransparentProxy();
            var factoryField=AccessTools.Field(typeof(MyTextSurfaceScriptFactory),"m_instance");
            var previous=factoryField.GetValue(null);
            try
            {
                var factory=new MyTextSurfaceScriptFactory(); factoryField.SetValue(null,factory);
                factory.RegisterFromAssembly(typeof(VrTouchTest).Assembly);
                foreach(string id in new[] { "SEVR_TouchTest","SEVR_Stopwatch" })
                using(var app=MyTextSurfaceScriptFactory.CreateScript(id,surface,block,new Vector2(512)))
                {
                    Require(app!=null,"Starter app absent from native LCD factory"); app.Run();
                    Require(surfaceProxy.Sprites.Count>=7,"Starter app did not submit a native sprite frame");
                }
                log("PASS native LCD factory: both selectable starter apps construct and render native sprite frames without a world.");
            }
            finally { factoryField.SetValue(null,previous); }

            string path=Environment.GetEnvironmentVariable("SEVR_TOUCH_API_FIXTURE");
            if(string.IsNullOrEmpty(path)) { log("TouchScreenAPI source fixture not requested (SEVR_TOUCH_API_FIXTURE)."); return; }
            if(!File.Exists(path)) throw new FileNotFoundException("TouchScreenAPI fixture",path);
            var assembly=Assembly.LoadFrom(path); var type=assembly.GetType("Lima.Touch.TouchScreen",true);
            var sessionType=assembly.GetType("Lima.Touch.TouchSession",true);
            Require(AccessTools.Field(sessionType,"Instance")!=null && AccessTools.Field(sessionType,"TouchMan")!=null &&
                AccessTools.Property(sessionType,"ModEnabled")?.PropertyType==typeof(bool),"TouchScreenAPI live session discovery contract changed");
            var native=FormatterServices.GetUninitializedObject(type);
            void Set(string name,object value) => AccessTools.Property(type,name).SetValue(native,value);
            Set("Block",block); Set("Surface",surface); Set("Index",0);
            Set("Viewport",new RectangleF(0,128,512,256));
            Set("Enabled",true);
            object coords=Activator.CreateInstance(assembly.GetType("Lima.Touch.SurfaceCoords",true),"LCD",0,
                new Vector3(-.5f,.3f,0),new Vector3(-.5f,-.3f,0),new Vector3(.5f,-.3f,0));
            Set("Coords",coords);
            var buttonType=assembly.GetType("Lima.Touch.ButtonState",true);
            foreach(string name in new[] { "Mouse1","Mouse2","Mouse3" }) AccessTools.Field(type,name).SetValue(native,Activator.CreateInstance(buttonType));
            var wrapperType=typeof(TouchScreenBridge).GetNestedType("Screen",BindingFlags.NonPublic);
            var wrapper=Activator.CreateInstance(wrapperType,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance,null,new[] { native },null);
            for(int shape=0;shape<2;shape++)
            {
                Vector3 tl=shape==0 ? new Vector3(-.5f,.3f,0) : new Vector3(-.058204f,-.474206f,-.010117f);
                Vector3 bl=shape==0 ? new Vector3(-.5f,-.3f,0) : new Vector3(-.044999f,-.547803f,.142677f);
                Vector3 br=shape==0 ? new Vector3(.5f,-.3f,0) : new Vector3(.044999f,-.547803f,.142677f);
                Set("Coords",Activator.CreateInstance(assembly.GetType("Lima.Touch.SurfaceCoords",true),"LCD",0,tl,bl,br));
                for(int i=0;i<12;i++)
                {
                    blockProxy.World=MatrixD.CreateFromYawPitchRoll(i*.3,i*.2,i*.1); blockProxy.World.Translation=new Vector3D(2e6,-3e6,4e6);
                    var plane=(SurfaceView)wrapperType.GetMethod("Plane").Invoke(wrapper,null);
                    var point=Vector3D.Transform(new Vector3D(plane.Width*.2,-plane.Height*.25,0),plane.Pose);
                    Set("Intersection",point);
                    for(int rotation=0;rotation<4;rotation++)
                    {
                        AccessTools.Field(type,"_rotation").SetValue(native,rotation);
                        var pixel=(Vector2)AccessTools.Method(type,"UpdateScreenCoord").Invoke(native,null);
                        var expected=new[] { new Vector2(.7f,.75f),new Vector2(.75f,.3f),new Vector2(.3f,.25f),new Vector2(.25f,.7f) }[rotation]*new Vector2(512,256)+new Vector2(0,128);
                        Require(Vector2.Distance(pixel,expected)<.004,"Actual TouchScreenAPI surface/pixel mapping mismatch, shape "+shape+", rotation "+rotation);
                    }
                }
            }
            object mouse=AccessTools.Field(type,"Mouse1").GetValue(native);
            var buttons=wrapperType.GetMethod("Buttons");
            bool State(string name) => (bool)AccessTools.Property(buttonType,name).GetValue(mouse);
            buttons.Invoke(wrapper,new object[] { true,true,false }); Require(State("JustPressed"),"Mod fresh press lost");
            buttons.Invoke(wrapper,new object[] { true,true,false }); Require(!State("JustPressed"),"Mod held press repeated");
            buttons.Invoke(wrapper,new object[] { false,false,false });
            buttons.Invoke(wrapper,new object[] { true,false,false }); Require(!State("JustReleased"),"Mod resumed with accidental release-click");
            buttons.Invoke(wrapper,new object[] { true,true,false }); Require(State("JustPressed"),"Mod fresh press after cancellation lost");
            buttons.Invoke(wrapper,new object[] { true,false,false }); Require(State("JustReleased"),"Mod deliberate release lost");
            log("PASS actual TouchScreenAPI source fixture: reflection contract, 96 rectangular/trapezoid, rotated and moving large-world LCD UV/pixel mappings, native button hold/cancel/rearm/release semantics. No saved world loaded.");
        }
    }
}
