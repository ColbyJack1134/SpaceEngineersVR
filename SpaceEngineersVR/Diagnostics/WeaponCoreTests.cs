#if SEVR_DIAGNOSTICS
using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using Sandbox.Game.GUI.HudViewers;
using SpaceEngineersVR.Player;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    // WeaponCore / Defense Shields support without either mod loaded: trigger ownership, the arm-then-pull flow, mod type
    // resolution across world reloads, and the WeaponCore marker layout. Firing a real WeaponCore weapon needs the game.
    internal static class WeaponCoreTests
    {
        private static void Require(bool value,string reason) { if(!value) throw new Exception(reason); }

        internal static void Run(Action<string> log)
        {
            TriggerOwnership(); log("PASS WeaponCore trigger: panel pulls never reach firing, between keys included");
            ArmThenPull(); log("PASS WeaponCore arm: held trigger ignored, fresh pull completes once, UI pulls and expiry do not");
            LiveTypes(); log("PASS WeaponCore reload: newest live mod session wins, stale copies and property instances resolve");
            MarkerLayout(); log("PASS WeaponCore markers: labelled, apart, sided, edge arrow for an off-screen lock, no 2 km or contacts cut");
        }

        private static void TriggerOwnership()
        {
            Require(!SpatialUi.SwallowsTrigger(true,-1,false,false,-1),"Trigger swallowed with no panel under the ray");
            Require(SpatialUi.SwallowsTrigger(true,3,false,false,-1),"Pull on a wrist key reached firing");
            Require(SpatialUi.SwallowsTrigger(true,-1,true,false,-1),"Pull between wrist keys reached firing");
            Require(SpatialUi.SwallowsTrigger(true,-1,false,true,-1),"Pull between seat panel keys reached firing");
            Require(SpatialUi.SwallowsTrigger(false,-1,false,false,2),"Held seat key released the trigger to firing");
            Require(!SpatialUi.SwallowsTrigger(false,3,true,true,-1),"Hover alone blocked a trigger that was not pulled");
        }

        private static void ArmThenPull()
        {
            var arm=new TriggerArm(10);
            try
            {
                Require(!arm.Pulled(false,true,true),"Disarmed trigger arm completed");
                arm.Arm();
                Require(TriggerArm.AnyArmed,"Armed action did not hold off firing");
                // The pull that pressed the arming button is still held.
                Require(!arm.Pulled(true,false,true),"Arming pull completed the action");
                Require(!arm.Pulled(true,true,true),"Held arming pull completed the action");
                Require(!arm.Pulled(false,false,true),"Release completed the action");
                Require(!arm.Pulled(true,true,false),"Pull owned by a UI completed the action");
                Require(arm.Armed,"UI pull cleared the arm");
                Require(arm.Pulled(true,true,true),"Fresh pull did not complete the action");
                Require(!arm.Armed && !arm.Pulled(true,true,true),"Completed action fired twice");
                var expiring=new TriggerArm(0);
                expiring.Arm(); Thread.Sleep(20);
                Require(expiring.Expired,"Arm outlived its time");
                expiring.Disarm();
            }
            finally { arm.Disarm(); }
            Require(!TriggerArm.AnyArmed,"Disarmed actions still hold off firing");
        }

        // Mods are recompiled per world and old assemblies stay loaded: two dynamic assemblies stand in for two worlds.
        private static void LiveTypes()
        {
            const string name="SevrTests.WeaponCoreSession";
            Type Build(string assembly,bool property)
            {
                var module=AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(assembly),AssemblyBuilderAccess.Run).DefineDynamicModule(assembly);
                var type=module.DefineType(name,TypeAttributes.Public|TypeAttributes.Class);
                if(!property) { type.DefineField("I",typeof(object),FieldAttributes.Public|FieldAttributes.Static); return type.CreateType(); }
                // Defense Shields exposes its session as a static property.
                var backing=type.DefineField("instance",typeof(object),FieldAttributes.Public|FieldAttributes.Static);
                var getter=type.DefineMethod("get_Instance",MethodAttributes.Public|MethodAttributes.Static|MethodAttributes.SpecialName|MethodAttributes.HideBySig,typeof(object),Type.EmptyTypes);
                var il=getter.GetILGenerator(); il.Emit(OpCodes.Ldsfld,backing); il.Emit(OpCodes.Ret);
                type.DefineProperty("Instance",PropertyAttributes.None,typeof(object),Type.EmptyTypes).SetGetMethod(getter);
                return type.CreateType();
            }
            var first=Build("SevrTests.World1",false);
            var second=Build("SevrTests.World2",false);
            try
            {
                first.GetField("I").SetValue(null,new object());
                Require(WeaponCoreInternals.LiveType(name,"I")==first,"Only live session not found");
                second.GetField("I").SetValue(null,new object());
                Require(WeaponCoreInternals.LiveType(name,"I")==second,"Stale session from a previous world chosen over the newest");
                first.GetField("I").SetValue(null,null);
                second.GetField("I").SetValue(null,null);
                Require(WeaponCoreInternals.LiveType(name,"I")==null,"Session found with no world loaded");
            }
            finally { first.GetField("I").SetValue(null,null); second.GetField("I").SetValue(null,null); }
            var shields=Build("SevrTests.World3",true);
            var session=new object();
            shields.GetField("instance").SetValue(null,session);
            try
            {
                Require(WeaponCoreInternals.StaticReader(shields,"Instance")?.Invoke()==session,"Static property instance not read");
                Require(WeaponCoreInternals.StaticReader(shields,"Missing")==null,"Missing member produced a reader");
            }
            finally { shields.GetField("instance").SetValue(null,null); }
        }

        private static WorldMarkers.Marker Marker(string id,string name,Vector3D position,MatrixD head,float span=1,bool pinned=true,string symbol="brackets",string side=null,string locked=null)
        {
            var color=new Vector4(1,.4f,.2f,1);
            return new WorldMarkers.Marker {Kind="WeaponCore",Id=id,Name=name,Relation="Enemies",Color=color,FontColor=color,Pinned=pinned,LockState=locked,
                Position=position,Distance=Vector3D.Distance(position,head.Translation),Symbol=symbol,Span=span,LabelSide=side};
        }

        private static void MarkerLayout()
        {
            var head=MatrixD.CreateTranslation(1000000,2000000,3000000);
            var now=DateTime.UtcNow;
            var options=new SignalLayout.Options();
            Vector3D At(double x,double y,double z) => Vector3D.Transform(new Vector3D(x,y,z),head);

            // A locked ship with the aim, paint preview and paint mark all on it, and two threats close by.
            var markers=new[] {
                Marker("wcfocus:1","LOCKED  Raider Frigate  T7  S 74%",At(0,0,-1500),head,4,locked:"Locked"),
                Marker("wcaim","AIM",At(0,0,-1500),head,1,symbol:"cross",side:"below"),
                Marker("wcpainted","PAINTED",At(-10,-8,-1500),head,.9f,side:"upleft"),
                Marker("wcpaint","PAINT  pull trigger",At(12,6,-1500),head,1.2f,symbol:"cross",side:"upright"),
                Marker("wcthreat:2","Interceptor  T5  S down",At(25,30,-1400),head,1.7f),
                Marker("wcthreat:3","Drone  T2",At(-40,-20,-1600),head,1.7f),
            };
            var entries=SignalLayout.Build(new WorldMarkers.View(markers,now) {Mode=MyHudMarkerRender.SignalMode.FullDisplay},head,options,now);
            Require(markers.All(m=>entries.Any(e=>e.Primary.Id==m.Id)),"WeaponCore marker dropped from the layout");
            var named=entries.Where(e=>e.Labels.Any(l=>!string.IsNullOrEmpty(l.Name))).Select(e=>e.Primary.Id).ToArray();
            Require(markers.All(m=>named.Contains(m.Id)),"WeaponCore marker drawn without its name");
            var ink=entries.SelectMany(e=>e.Labels.SelectMany(SignalLayout.Ink)).ToArray();
            for(int i=0;i<ink.Length;i++) for(int j=i+1;j<ink.Length;j++)
                Require(!SignalLayout.Overlaps(ink[i],ink[j]),"WeaponCore marker names overlap");
            SignalLayout.Entry Entry(string id) => entries.Single(e=>e.Primary.Id==id);
            SignalLayout.Label Name(string id) => Entry(id).Labels.First(l=>!string.IsNullOrEmpty(l.Name));
            var focus=Entry("wcfocus:1");
            Require(Name("wcfocus:1").NameY<focus.Point.Y,"Lock name not above its brackets");
            Require(focus.SymbolBounds.Width>Entry("wcpainted").SymbolBounds.Width,"Bracket size ignores the target span");
            Require(Name("wcaim").NameY>Entry("wcaim").Point.Y,"AIM name not below its cross");
            Require(Name("wcpainted").Bounds.X+Name("wcpainted").Bounds.Width<=Entry("wcpainted").Point.X+1e-4f,"PAINTED name not to the left of its mark");
            Require(Name("wcpaint").Bounds.X>=Entry("wcpaint").Point.X-1e-4f,"PAINT name not to the right of its cross");

            // Out of view: only the lock stays, as an arrow inside the view edge pointing toward it.
            var behind=new[] {Marker("wcfocus:4","LOCKED  Gunboat",At(-1000,0,600),head,2.2f,locked:"Locked"),Marker("wcthreat:5","Drone",At(800,0,700),head,1.7f)};
            entries=SignalLayout.Build(new WorldMarkers.View(behind,now) {Mode=MyHudMarkerRender.SignalMode.FullDisplay},head,options,now);
            Require(entries.Length==1 && entries[0].Primary.Id=="wcfocus:4","Off-screen lock missing or off-screen threat shown");
            var arrow=entries[0];
            Require(arrow.Arrow.X<0,"Edge arrow does not point toward the lock");
            Require(Math.Abs(arrow.Point.X)<=options.LimitX*.8f+1e-3f && Math.Abs(arrow.Point.Y)<=options.LimitY*.8f+1e-3f,"Edge arrow outside the view");
            Require(arrow.Labels.Any(l=>!string.IsNullOrEmpty(l.Name)),"Edge arrow lost the lock name");

            // Beyond 2 km without detailed markers, and with contacts hidden.
            var far=Marker("wcthreat:6","Carrier  T8",At(0,0,-5000),head,1.7f,pinned:false);
            var vanilla=new WorldMarkers.Marker {Kind="Target",Id="turret-target",Relation="Enemies",Position=At(0,40,-5000),Distance=5000,Color=Vector4.One};
            entries=SignalLayout.Build(new WorldMarkers.View(new[] {far,vanilla},now) {Mode=MyHudMarkerRender.SignalMode.NoNames},head,options,now);
            Require(entries.Any(e=>e.Primary.Id==far.Id),"WeaponCore threat hidden beyond 2 km");
            Require(entries.All(e=>e.Primary.Id!=vanilla.Id),"Vanilla turret target lost its 2 km limit");
            var contact=new WorldMarkers.Marker {Kind="LargeEntity",Id="contact",Relation="Enemies",Position=At(0,-40,-1000),Distance=1000,Color=Vector4.One,Name="Contact"};
            entries=SignalLayout.Build(new WorldMarkers.View(new[] {far,contact},now) {Mode=MyHudMarkerRender.SignalMode.FullDisplay},head,new SignalLayout.Options {Contacts=false},now);
            Require(entries.Any(e=>e.Primary.Id==far.Id) && entries.All(e=>e.Primary.Id!=contact.Id),"Contact filter applied to WeaponCore markers");
        }
    }
}
#endif
