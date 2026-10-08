using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using HarmonyLib;
using Sandbox.ModAPI;
using SpaceEngineersVR.Player;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class ArthurNativeInputTests
    {
        private sealed class InputProxy : RealProxy
        {
            internal InputProxy(Type type):base(type) { }
            public override IMessage Invoke(IMessage message)
            {
                var call=(IMethodCallMessage)message;
                if(call.MethodName!="GetGameControl") throw new Exception("Unexpected Arthur native input query: "+call.MethodName);
                return new ReturnMessage(null,null,0,call.LogicalCallContext,call);
            }
        }
        private sealed class Screen : RealProxy
        {
            private readonly Type screenType;
            internal readonly IList Entries;
            internal Vector2 Cursor=new Vector2(10,10),ScrolledAt;
            internal int Scrolls;
            internal Screen(Type type,Type control):base(type)
            { screenType=type; Entries=(IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(control)); }
            public override IMessage Invoke(IMessage message)
            {
                var call=(IMethodCallMessage)message; object result=null;
                switch(call.MethodName)
                {
                    case "GetType": result=screenType; break;
                    case "get_InteractiveEntries": result=Entries; break;
                    case "get_CursorPosition": result=Cursor; break;
                    case "get_HitTestOffset": result=Vector2.Zero; break;
                    case "LookAt": Cursor=(Vector2)call.Args[0]; break;
                    case "MouseScroll": Scrolls++; ScrolledAt=Cursor; break;
                    case "PlaySounds": break;
                    default: throw new Exception("Unexpected Arthur screen query: "+call.MethodName);
                }
                return new ReturnMessage(result,null,0,call.LogicalCallContext,call);
            }
        }
        private static bool Allow(ref bool __result) { __result=true; return false; }
        private static bool Drag(object data,object sender,Vector2 delta) { drags++; return true; }
        private static int drags;
        private static void Require(bool value,string message) => LcdInputTests.Require(value,message);
        internal static void Run(Assembly assembly,Type moduleType,Action<string> log)
        {
            var bridge=typeof(ArthurLcdBridge);
            var allowed=AccessTools.PropertyGetter(bridge,"Allowed");
            var harmony=new Harmony("SpaceEngineersVR.ArthurNativeInputFixture");
            var gateway=AccessTools.Field(typeof(MyAPIGateway),"Input");
            object previousInput=gateway.GetValue(null);
            var saved=new Dictionary<string,object>();
            foreach(string name in new[] {"nativeModule","selected","selectedInput","hand","owner","dispatch","prepared","dispatched"})
                saved[name]=AccessTools.Field(bridge,name).GetValue(null);
            Action<string,object> set=(name,value)=>AccessTools.Field(bridge,name).SetValue(null,value);
            try
            {
                if(previousInput==null) gateway.SetValue(null,new InputProxy(gateway.FieldType).GetTransparentProxy());
                harmony.Patch(allowed,new HarmonyMethod(typeof(ArthurNativeInputTests),nameof(Allow)));
                var module=Activator.CreateInstance(moduleType);
                var update=AccessTools.Method(moduleType,"UpdateClickState");
                var prefix=AccessTools.Method(bridge,"UpdatePrefix");
                var postfix=AccessTools.Method(bridge,"UpdatePostfix");
                set("nativeModule",null); set("prepared",0L); set("dispatched",0L);
                Require(!(bool)prefix.Invoke(null,new[] {module}),"First native update used an unprepared input sample");
                foreach(bool nativeFirst in new[] {false,true})
                {
                    long sequence=(long)AccessTools.Field(bridge,"prepared").GetValue(null);
                    if(nativeFirst) Require(!(bool)prefix.Invoke(null,new[] {module}),"Native update reacquired input between Main samples");
                    set("prepared",sequence+1);
                    Require((bool)prefix.Invoke(null,new[] {module}),"Prepared sample was not dispatched");
                    postfix.Invoke(null,null);
                    Require(!(bool)prefix.Invoke(null,new[] {module}),"Prepared sample dispatched twice");
                }
                var eyeType=assembly.GetType("LcdMod.Client.Utility.IEyeTracking",true);
                var rectangle=assembly.GetType("LcdMod.Client.Gui.ControlsTemplates.RectangleControl",true);
                var screen=new Screen(eyeType,rectangle.BaseType.BaseType);
                var surface=screen.GetTransparentProxy();
                int clicks=0,secondaryClicks=0,begins=0,ends=0;
                var control=Activator.CreateInstance(rectangle,new object[] {new RectangleF(0,0,100,100),null,null,new Action<object,object>((_,__)=>clicks++),null});
                screen.Entries.Add(control);
                Action<string,object> property=(name,value)=>AccessTools.Property(rectangle,name).SetValue(control,value);
                property("OnSecondaryClick",new Action<object,object>((_,__)=>secondaryClicks++));
                var state=new LcdInput(); var c=LcdInputTests.ControlsForTest(); var now=DateTime.UtcNow;
                Action<float,float,bool> sample=(trigger,grip,near)=> {
                    LcdInputTests.Poll(c,trigger,grip,alias:true);
                    state.Update(state.Read(c,true,false),true,near,now);
                    state.Consume(c,true);
                    set("selected",surface); set("selectedInput",state);
                    Require(!c.Primary.IsPressed && !c.Interact.HasPressed && !c.Secondary.IsPressed,"Gameplay aliases escaped before native dispatch");
                    update.Invoke(module,new[] {control,surface,surface});
                    now=now.AddMilliseconds(20);
                };
                foreach(bool near in new[] {false,true})
                {
                    state.Cancel(); sample(0,0,near);
                    int before=clicks;
                    sample(.1f,0,near); sample(.3f,0,near); sample(.8f,0,near);
                    Require(clicks==before,"Release-click fired on press");
                    sample(.8f,0,near); sample(0,0,near);
                    Require(clicks==before+1,"Native primary release did not click exactly once");
                    int secondaryBefore=secondaryClicks;
                    sample(0,.9f,near); sample(0,.9f,near); sample(0,0,near);
                    Require(secondaryClicks==secondaryBefore+1 && clicks==before+1,"Native grip was not exclusively secondary");
                }
                int cancelledBefore=clicks;
                sample(.8f,0,false);
                AccessTools.Method(bridge,"ResetInput").Invoke(null,null);
                state.Cancel(); sample(0,0,false);
                Require(clicks==cancelledBefore && AccessTools.Field(moduleType,"_pressedClickable").GetValue(module)==null,
                    "Cancelled press clicked on the next target/release");
                property("ClickOnPress",true);
                int pressBefore=clicks;
                sample(.8f,0,false);
                Require(clicks==pressBefore+1,"Native ClickOnPress was delayed");
                sample(0,0,false);
                Require(clicks==pressBefore+1,"Native ClickOnPress also clicked on release");
                property("ClickOnPress",false); property("Draggable",true); property("SecondaryDraggable",true);
                property("OnBeginDrag",new Action<object,object>((_,__)=>begins++));
                property("OnEndDrag",new Action<object,object>((_,__)=>ends++));
                property("OnDrag",Delegate.CreateDelegate(AccessTools.Property(rectangle,"OnDrag").PropertyType,AccessTools.Method(typeof(ArthurNativeInputTests),nameof(Drag))));
                drags=0;
                sample(.8f,0,false); screen.Cursor=new Vector2(25,20); sample(.8f,0,false); sample(0,0,false);
                Require(begins==1 && ends==1 && drags==1 && clicks==pressBefore+1,"Native primary drag froze, repeated, or clicked on release");
                sample(0,.9f,true); screen.Cursor=new Vector2(40,30); sample(0,.9f,true);
                Require(begins==2 && drags==2,"Native secondary drag was not maintained after consumption");
                // Use the production flush with mouse-down still prepared, as on failure/reset.
                AccessTools.Method(bridge,"ResetInput").Invoke(null,null);
                Require(ends==2 && AccessTools.Field(moduleType,"_draggingControl").GetValue(module)==null &&
                    !(bool)AccessTools.Field(moduleType,"_secondaryWasPressed").GetValue(module),"Bridge cancellation left native drag/button state behind");
                state.Cancel(); sample(0,0,false);
                LcdInputTests.Poll(c,.1f,0,axis:1);
                state.Update(state.Read(c,true,false),true,false,now); state.Consume(c,true);
                set("dispatch",false);
                var scroll=AccessTools.Method(bridge,"ScrollPrefix");
                scroll.Invoke(null,new[] {surface});
                Require(screen.Scrolls==0,"Scroll escaped before native cursor dispatch");
                eyeType.GetMethod("LookAt").Invoke(surface,new object[] {new Vector2(70,60)});
                set("dispatch",true); scroll.Invoke(null,new[] {surface}); scroll.Invoke(null,new[] {surface});
                Require(screen.Scrolls==1 && screen.ScrolledAt==new Vector2(70,60) && c.WalkRotate.Position.Y==0,"Scroll used stale cursor coordinates or dispatched twice");
                log("PASS actual Arthur input dispatch: prepared-sample ordering/bootstrap, production alias consumption, native primary/secondary release, ClickOnPress, moving primary/secondary drag, cancellation flush and cursor-first scroll.");
            }
            finally
            {
                AccessTools.Method(bridge,"ResetInput").Invoke(null,null);
                harmony.Unpatch(allowed,HarmonyPatchType.All,harmony.Id);
                gateway.SetValue(null,previousInput);
                foreach(var item in saved) set(item.Key,item.Value);
            }
        }
    }
}
