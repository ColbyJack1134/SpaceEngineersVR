using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Sandbox.Game.Entities;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Plugin;
using SpaceEngineersVR.Util;
using VRage.Input;
using VRage.Game;
using VRage.Utils;
using VRageRender;
using VRageMath;

namespace SpaceEngineersVR.Patches
{
    // Input goes through IMyInput on Rich HUD Master ticks; the game itself never sees the clicks or scrolls.
    internal static class BuildVisionBridge
    {
        internal const string Title="Build Vision";
        private const double OpenRange=10;
        private static readonly List<(MethodBase method,MethodInfo patch)> patched=new List<(MethodBase,MethodInfo)>();
        private static readonly HashSet<Assembly> scanned=new HashSet<Assembly>();
        private static readonly List<Type> spaces=new List<Type>(),binds=new List<Type>();
        private static DateTime retry;
        private static Type space;
        private static FieldInfo instance,targetTick;
        private static PropertyInfo menuState;
        private static MethodInfo tryOpen,closeMenu,tryTarget;
        private static FieldInfo menuField,wheelField,activeField;
        private static bool ticking,armed;

        private static LineD? ray;
        private static IMyTerminalBlock target;
        internal static bool Showing { get; private set; }

        // Tick-scoped injection: scroll shows for one tick with a gap after it, a click is held two ticks and released for two.
        private static int scroll,scrollTicks,wheelDelta;
        private static readonly Queue<MyKeys> taps=new Queue<MyKeys>();
        private static MyKeys tap;
        private static int tapTicks;
        private static int direction;
        private static DateTime repeatAt;
        private const int WheelMenuOpen=0x2,PropertyOpen=0x8,NoValue=0,BoolValue=1,FloatValue=5,TextValue=6;
        // Build Vision's wheel is 512 units across; this matches the outer ring of the SEVR wheels.
        private const double Meters=.40/512;
        private static MatrixD? anchor;
        private static bool closing,dragging;
        // Build Vision can draw once more after closing; until then its HUD keeps the hand pose instead of flashing at the head-locked spot.
        private static DateTime lingerUntil;
        // Read on the render thread; 0 hides the ray.
        private static volatile float rayDistance;
        internal static bool SliderOpen { get; private set; }
        private static int lastPointed=-1;
        internal static int Page,Pages;
        private static readonly MyStringId dot=MyStringId.GetOrCompute("WhiteDot");
        private static DateTime pointedAt;

        internal static bool Active => space!=null && ticking && instance?.GetValue(null)!=null;

        internal static void RefreshRegistrations()
        {
            if(!Main.VrActive || MySession.Static==null || DateTime.UtcNow<retry) return;
            retry=DateTime.UtcNow.AddSeconds(1);
            foreach(var assembly in Util.Util.NewestAssemblies())
            {
                if(!scanned.Add(assembly) || assembly.IsDynamic) continue;
                try
                {
                    var hud=assembly.GetType("DarkHelmet.BuildVision2.QuickActionHudSpace",false);
                    if(hud!=null) spaces.Add(hud);
                    var manager=assembly.GetType("RichHudFramework.UI.Server.BindManager",false);
                    if(manager!=null) binds.Add(manager);
                }
                catch { }
            }
            if(!ticking)
                foreach(var manager in binds)
                {
                    if(AccessTools.Field(manager,"_instance")?.GetValue(null)==null) continue;
                    var handle=AccessTools.Method(manager,"HandleInput",Type.EmptyTypes);
                    if(handle==null) { Logger.Info("Rich HUD Master input update not found; Build Vision stays desktop only"); binds.Clear(); break; }
                    if(!Attach(() => Patch(handle,nameof(Tick),null),"Rich HUD Master input")) { binds.Clear(); break; }
                    ticking=true;
                    break;
                }
            if(space!=null && instance?.GetValue(null)!=null) return;
            foreach(var hud in spaces)
            {
                var field=AccessTools.Field(hud,"instance");
                if(field?.GetValue(null)==null) continue;
                Bind(hud,field);
                return;
            }
        }

        private static void Bind(Type hud,FieldInfo field)
        {
            spaces.Remove(hud);
            var finder=hud.Assembly.GetType("DarkHelmet.BuildVision2.BlockFinder",false);
            var update=finder==null ? null:AccessTools.Method(finder,"TryUpdateTargets",new[] {typeof(LineD)});
            var tick=AccessTools.Field(hud,"targetTick");
            var state=AccessTools.Property(hud,"MenuState");
            var open=AccessTools.Method(hud,"TryOpenMenu",Type.EmptyTypes);
            var close=AccessTools.Method(hud,"CloseMenu",Type.EmptyTypes);
            var targeted=AccessTools.Method(hud,"TryGetTargetedBlock",new[] {typeof(double),typeof(IMyTerminalBlock).MakeByRefType()});
            var input=AccessTools.TypeByName("VRage.Input.MyVRageInput");
            var map=input?.GetInterfaceMap(typeof(VRage.ModAPI.IMyInput));
            MethodInfo Implementation(string name,params Type[] parameters)
            {
                if(map==null) return null;
                int index=Array.FindIndex(map.Value.InterfaceMethods,m => m.Name==name && m.GetParameters().Select(p => p.ParameterType).SequenceEqual(parameters));
                return index<0 ? null:map.Value.TargetMethods[index];
            }
            var menu=AccessTools.Field(hud,"quickActionMenu");
            var wheelMenu=menu==null ? null:AccessTools.Field(menu.FieldType,"propertyWheel");
            var active=wheelMenu==null ? null:AccessTools.Field(wheelMenu.FieldType,"activeWheel");
            var keyPress=Implementation("IsKeyPress",typeof(MyKeys));
            var wheel=Implementation("DeltaMouseScrollWheelValue");
            if(update==null || tick?.FieldType!=typeof(int) || state==null || open==null || close==null || targeted==null || active==null || keyPress==null || wheel==null)
            { Logger.Info("Build Vision members not found; left desktop only in VR"); return; }
            if(!Attach(() => {
                Patch(update,nameof(TargetRay),null);
                Patch(keyPress,null,nameof(KeyPress));
                Patch(wheel,null,nameof(Wheel));
            },"Build Vision")) return;
            space=hud; instance=field; targetTick=tick; menuState=state; tryOpen=open; closeMenu=close; tryTarget=targeted;
            menuField=menu; wheelField=wheelMenu; activeField=active;
            Logger.Info("Build Vision adapter attached to "+hud.Assembly.GetName().Name);
        }
        private static bool Attach(Action attach,string part)
        {
            int start=patched.Count;
            try { attach(); return true; }
            catch(Exception ex) { Rollback(start); Logger.Warning(ex,part+" VR adapter unavailable"); return false; }
        }
        private static void Patch(MethodBase method,string prefix,string postfix)
        {
            var patch=AccessTools.Method(typeof(BuildVisionBridge),prefix ?? postfix);
            patched.Add((method,patch));
            Common.Plugin.Harmony.Patch(method,prefix==null ? null:new HarmonyMethod(patch),postfix==null ? null:new HarmonyMethod(patch));
        }
        private static void Rollback(int start)
        {
            foreach(var (method,patch) in patched.Skip(start).ToArray()) Common.Plugin.Harmony.Unpatch(method,patch);
            patched.RemoveRange(start,patched.Count-start);
        }
        internal static void Reset()
        {
            Release(); ray=null; target=null;
            Rollback(0); scanned.Clear(); spaces.Clear(); binds.Clear(); retry=DateTime.MinValue; ticking=false;
            space=null; instance=targetTick=null; menuState=null; tryOpen=closeMenu=tryTarget=null; menuField=wheelField=activeField=null;
        }

        // Build Vision's own target test along the action hand's ray.
        internal static bool Capture()
        {
            ray=null; target=null;
            var character=MySession.Static?.LocalCharacter;
            if(!Active || !BlockActions.CharacterAvailable(character) || MyCubeBuilder.Static?.CubeBuilderState?.CurrentBlockDefinition!=null ||
                !HandInteraction.TryActionInteractionRay(out var line)) return false;
            ray=line;
            var args=new object[] {OpenRange,null};
            bool found;
            armed=true;
            try { found=(bool)tryTarget.Invoke(null,args); }
            finally { armed=false; }
            if(!found || !(args[1] is IMyTerminalBlock block)) { ray=null; return false; }
            target=block;
            return true;
        }
        internal static bool Open(Matrix hand)
        {
            if(!Active || ray==null || target==null || target.Closed) return false;
            SetAnchor(hand);
            var hud=instance.GetValue(null);
            // Build Vision only opens on every fifth input tick; its counter advances only inside its own input update.
            targetTick.SetValue(hud,0);
            armed=true;
            try { tryOpen.Invoke(null,null); }
            finally { armed=false; }
            // The wheel body refreshes its text every few frames and still holds the last close's "[Incomplete]"; refresh on the first frame.
            ModReflection.Set(ModReflection.Get(wheelField.GetValue(menuField.GetValue(hud)),"wheelBody"),"tick",0);
            Showing=MenuOpen;
            return Showing;
        }
        private static bool MenuOpen => Active && Convert.ToInt32(menuState.GetValue(null))!=0;
        internal static bool StillOpen => Showing && MenuOpen;
        internal static void Close()
        {
            if(Showing && MenuOpen) closeMenu.Invoke(null,null);
            Release();
        }
        private static void Release()
        {
            if(Showing) lingerUntil=DateTime.UtcNow.AddSeconds(.2);
            Showing=closing=dragging=SliderOpen=false; rayDistance=0; taps.Clear(); scroll=scrollTicks=tapTicks=0; tap=MyKeys.None; direction=0; lastPointed=-1;
        }
        private static void SetAnchor(Matrix hand) => anchor=CameraRig.DeviceWorld(hand);
        private static int State => Convert.ToInt32(menuState.GetValue(null));
        // A property editor or the list is open; the stick steps instead of pointing.
        internal static bool Editing => Showing && ((State & PropertyOpen)!=0 || Listing);
        private static bool Listing => (State & WheelMenuOpen)==0;
        // The release-chosen entry's click has been delivered.
        internal static bool Done => closing && taps.Count==0 && tapTicks==0;
        internal static void Update(Vector2 left,Vector2 right,bool select,bool back,bool grip,bool trigger,Matrix hand,Action<bool> step)
        {
            if(!Showing) return;
            SetAnchor(hand);
            var stick=left.LengthSquared()>right.LengthSquared() ? left:right;
            bool editing=Editing,listing=Listing;
            if(editing) Scroll(stick,grip,()=>step(stick==left));
            else if(Point(ActiveWheel,stick,select)) step(stick==left);
            SliderOpen=editing && !listing && Slider()!=null;
            if(SliderOpen) Drag(trigger); else { dragging=false; rayDistance=0; }
            var listed=listing ? ListEntry() : null;
            // Opening the list can also open its first entry's chat prompt; the VR keyboard replaces it, so list text prompts stay closed.
            if(IsText(ModReflection.Get(listed,"AssocMember")) && ModReflection.Get(listed,"PropertyOpen") is true) ModReflection.Set(listed,"PropertyOpen",false);
            if(select)
            {
                var entry=listing ? listed : editing ? null : ModReflection.Get(ActiveWheel,"HighlightedEntry");
                var member=ModReflection.Get(entry,listing ? "AssocMember":"BlockMember");
                if(IsText(member)) EditText(member);
                else taps.Enqueue(MyKeys.LeftButton);
            }
            if(back) taps.Enqueue(MyKeys.RightButton);
        }
        private static object ListEntry()
        {
            var list=ModReflection.Get(menuField.GetValue(instance.GetValue(null)),"propertyList");
            var body=ModReflection.Get(list,"listBody");
            if(body==null || !(ModReflection.Get(list,"selectionIndex") is int index) || index<0 || !(ModReflection.Get(body,"Count") is int count) || index>=count) return null;
            return body.GetType().GetProperty("Item",new[] {typeof(int)})?.GetValue(body,new object[] {index});
        }
        // Releasing the wheel button chooses the pointed entry like the SEVR wheels. Returns false when everything should close.
        internal static bool ReleaseWheel(Vector2 left,Vector2 right)
        {
            if(!Showing) return false;
            if(Editing || Listing) return true;
            if(Math.Max(left.LengthSquared(),right.LengthSquared())<.0625f) return false;
            var entry=ModReflection.Get(ActiveWheel,"HighlightedEntry");
            if(entry==null) return false;
            var member=ModReflection.Get(entry,"BlockMember");
            int type=member==null ? -1:Convert.ToInt32(ModReflection.Get(member,"ValueType"));
            if(type==TextValue) { EditText(member); return false; }
            taps.Enqueue(MyKeys.LeftButton);
            // Toggles and actions apply at once; editors, shortcuts and the list stay open for more input.
            closing=member!=null && (type==BoolValue || type==NoValue);
            return true;
        }
        private static object ActiveWheel => activeField.GetValue(wheelField.GetValue(menuField.GetValue(instance.GetValue(null))));
        // A centred stick clears the highlight; A within .6 s still takes the last entry, since the thumb leaves the stick to press it.
        private static bool Point(object wheel,Vector2 stick,bool select)
        {
            if(wheel==null) return false;
            if(stick.LengthSquared()<.36f)
            {
                bool pending=taps.Count>0 || tapTicks>0;
                var highlight=ModReflection.Get(wheel,"HighlightIndex") as int? ?? -1;
                if(select && highlight<0 && lastPointed>=0 && (DateTime.UtcNow-pointedAt).TotalSeconds<.6)
                    AccessTools.Method(wheel.GetType(),"SetHighlightAt")?.Invoke(wheel,new object[] {lastPointed});
                else if(highlight>=0 && !pending && stick.LengthSquared()<.0625f)
                {
                    lastPointed=highlight; pointedAt=DateTime.UtcNow;
                    AccessTools.Method(wheel.GetType(),"ClearHighlight")?.Invoke(wheel,null);
                }
                return false;
            }
            var direction=Vector2.Normalize(stick);
            int best=-1,index=0; float bestDot=.5f;
            foreach(var entry in (System.Collections.IEnumerable)ModReflection.Get(wheel,"EntryList"))
            {
                if(ModReflection.Get(entry,"Enabled") is true && ModReflection.Get(ModReflection.Get(entry,"Element"),"Offset") is Vector2 offset && offset.LengthSquared()>0)
                {
                    float dot=Vector2.Dot(Vector2.Normalize(offset),direction);
                    if(dot>bestDot) { bestDot=dot; best=index; }
                }
                index++;
            }
            if(best<0 || ModReflection.Get(wheel,"HighlightIndex") is int current && current==best) return false;
            AccessTools.Method(wheel.GetType(),"SetHighlightAt")?.Invoke(wheel,new object[] {best});
            return true;
        }
        // Grip steps a slider by a tenth of its range.
        private static void Scroll(Vector2 stick,bool grip,Action step)
        {
            float axis=Math.Abs(stick.Y)>=Math.Abs(stick.X) ? stick.Y:stick.X;
            int next=axis>.6f ? 1:axis<-.6f ? -1:0;
            var now=DateTime.UtcNow;
            if(next==0) { direction=0; return; }
            if(next!=direction) { direction=next; repeatAt=now.AddSeconds(.4); }
            else if(now<repeatAt) return;
            else repeatAt=now.AddSeconds(.1);
            if(grip && Nudge(next)) step();
            else if(Math.Abs(scroll)<2) { scroll+=next; step(); }
        }
        private static bool Nudge(int sign)
        {
            if(Listing)
            {
                var entry=ListEntry();
                var member=ModReflection.Get(entry,"PropertyOpen") is true ? ModReflection.Get(entry,"AssocMember") : null;
                if(member==null || Convert.ToInt32(ModReflection.Get(member,"ValueType"))!=FloatValue || !(ModReflection.Get(member,"MinValue") is float min) ||
                    !(ModReflection.Get(member,"MaxValue") is float max) || !(ModReflection.Get(member,"Value") is float value) || float.IsInfinity(max-min)) return false;
                return ModReflection.Set(member,"Value",MathHelper.Clamp(value+sign*.1f*(max-min),min,max));
            }
            var slider=Slider();
            return ModReflection.Get(slider,"Value") is float current && ModReflection.Set(slider,"Value",MathHelper.Clamp(current+sign*.1f,0f,1f));
        }
        // Build Vision copies this box's Value (0 to 1) into the property each tick.
        private static object Slider()
        {
            var widget=ModReflection.Get(ModReflection.Get(wheelField.GetValue(menuField.GetValue(instance.GetValue(null))),"wheelBody"),"ActiveWidget");
            return widget==null || CockpitRender.Find(widget.GetType(),"sliderBox")==null ? null:ModReflection.Get(widget,"sliderBox");
        }
        private static bool IsText(object member) => member!=null && Convert.ToInt32(ModReflection.Get(member,"ValueType"))==TextValue;
        private static bool EditText(object member)
        {
            if(!IsText(member) || !(ModReflection.Get(member,"PropName") is string id)) return false;
            var block=target;
            var property=block?.GetProperty(id)?.As<System.Text.StringBuilder>();
            if(property==null) return false;
            string draft=property.GetValue(block)?.ToString() ?? "";
            var owner=MySession.Static?.ControlledEntity;
            MenuKeyboard.Open(draft,value=>draft=value,()=>!block.Closed && ReferenceEquals(owner,MySession.Static?.ControlledEntity),null,"DONE",
                ()=> { if(!block.Closed) property.SetValue(block,new System.Text.StringBuilder(draft)); },4000);
            return MenuKeyboard.IsOpen;
        }
        private static void Drag(bool trigger)
        {
            var slider=Slider();
            var bar=slider==null ? null:ModReflection.Get(slider,"SliderBox");
            if(!trigger) dragging=false;
            bool shown=rayDistance>0;
            rayDistance=0;
            if(bar==null || !(ModReflection.Get(bar,"Position") is Vector2 position) || !(ModReflection.Get(bar,"Size") is Vector2 size) ||
                !Plane(out var plane,out var center) || !HandInteraction.TryCharacterPointerPose(Player.Player.HandR,out var pointer)) return;
            var normal=plane.Backward;
            double facing=Vector3D.Dot(pointer.Forward,normal);
            if(Math.Abs(facing)<1e-4) return;
            double distance=Vector3D.Dot(plane.Translation-pointer.Translation,normal)/facing;
            if(distance<=0 || distance>1.5) return;
            var hit=pointer.Translation+pointer.Forward*distance;
            var offset=hit-plane.Translation;
            var local=new Vector2((float)(Vector3D.Dot(offset,plane.Right)/Meters)+center.X,(float)(Vector3D.Dot(offset,plane.Up)/Meters)+center.Y);
            bool over=Math.Abs(local.X-position.X)<=size.X/2+16 && Math.Abs(local.Y-position.Y)<=size.Y/2+24;
            if(trigger && over) dragging=true;
            if(!over && !dragging) return;
            if(dragging || HandInteraction.ShowRay(Controls.Static.PointerPressure.RawPosition.X,shown)) rayDistance=(float)distance;
            if(dragging) ModReflection.Set(slider,"Value",MathHelper.Clamp((local.X-(position.X-size.X/2))/size.X,0f,1f));
        }
        internal static void DrawRay(SharpDX.Direct3D11.Texture2D target,MatrixD view,MatrixD projection)
        {
            float distance=rayDistance;
            if(distance<=0 || !TrackedArms.TryFreePointPose(Player.Player.HandR,out var aim)) return;
            PhysicalSurface.Draw(target,new[] {SpatialUi.RaySurface(aim,distance)},view,projection,PhysicalSurface.SceneDepth());
        }
        private static bool Plane(out MatrixD plane,out Vector2 center)
        {
            plane=MatrixD.Identity; center=Vector2.Zero;
            var current=anchor;
            if(!Showing && DateTime.UtcNow>=lingerUntil || current==null || !(ModReflection.Get(menuField.GetValue(instance.GetValue(null)),"Position") is Vector2 position)) return false;
            plane=current.Value; center=position;
            // The SEVR wheel ring is centred 27 of 1024 px above its .52 m quad.
            plane.Translation+=plane.Up*(.52*27/1024);
            return true;
        }
        // Dots at the SEVR wheels' height below the centre disc (156 of 1024 px on the .52 m quad, 24 px apart).
        internal static void DrawPageDots()
        {
            if(Pages<2 || !Showing || closing || !MenuOpen || Listing || !Plane(out var plane,out _)) return;
            const double Px=.52/1024;
            var cyan=new Vector4(0,1,1,1); var grey=new Color(112,128,144).ToVector4();
            for(int i=0;i<Pages && i<9;i++)
            {
                var origin=plane.Translation-plane.Up*(156*Px)+plane.Right*((i-(Math.Min(Pages,9)-1)/2.0)*24*Px)+plane.Backward*.002;
                MyTransparentGeometry.AddPointBillboard(dot,i==Page ? cyan:grey,origin,(float)(6.5*Px),0,blendType:MyBillboard.BlendTypeEnum.PostPP);
            }
        }
        // Rich HUD Master keys Build Vision's billboards by its PlaneToWorldRef array; toHand maps that plane onto the wheel anchor.
        internal static bool TryHandPlane(out MatrixD[] planes,out Vector2 center,out float reach,out MatrixD toHand)
        {
            planes=null; toHand=MatrixD.Identity; reach=0;
            if(!Plane(out var plane,out center)) return false;
            var hud=instance.GetValue(null);
            planes=ModReflection.Get(hud,"PlaneToWorldRef") as MatrixD[];
            if(planes==null || planes.Length==0 || !(ModReflection.Get(menuField.GetValue(hud),"Size") is Vector2 size)) return false;
            reach=size.Length()/2;
            toHand=MatrixD.Invert(planes[0])*MatrixD.CreateTranslation(-center.X,-center.Y,0)*MatrixD.CreateScale(Meters,Meters,0)*plane;
            return toHand.IsValid();
        }

        private static void Tick()
        {
            if(scrollTicks>0) scrollTicks--;
            else if(scroll!=0) { int sign=Math.Sign(scroll); scroll-=sign; wheelDelta=120*sign; scrollTicks=2; }
            if(tapTicks>0) tapTicks--;
            else if(taps.Count>0) { tap=taps.Dequeue(); tapTicks=4; }
            else tap=MyKeys.None;
        }
        private static void TargetRay(ref LineD targetLine)
        {
            if(!armed || ray==null) return;
            var line=ray.Value;
            targetLine=new LineD(line.From,line.From+line.Direction*targetLine.Length);
        }
        private static void KeyPress(MyKeys key,ref bool __result)
        {
            if(!__result && Showing && tapTicks>2 && key==tap) __result=true;
        }
        private static void Wheel(ref int __result)
        {
            if(__result==0 && Showing && scrollTicks==2) __result=wheelDelta;
        }
    }
}
