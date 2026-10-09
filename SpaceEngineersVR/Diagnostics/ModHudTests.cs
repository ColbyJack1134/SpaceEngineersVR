using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.ExceptionServices;
using HarmonyLib;
using SpaceEngineersVR.Patches;
using VRageMath;
using VRageRender;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class ModHudTests
    {
        internal static void Run(Action<string> log)
        {
            Registration(log);
            var origin=new Vector3D(80000,120000,-300000);
            var forward=Vector3D.Forward;
            foreach(var billboard in new MyBillboard[] {Quad(origin),Triangle(origin)})
            {
                var a=billboard.Position0; var b=billboard.Position1; var c=billboard.Position2; var d=billboard.Position3; var color=billboard.Color;
                Require(ModHud.Place(billboard) && billboard.Position0==a && billboard.Position1==b && billboard.Position2==c && billboard.Position3==d && billboard.Color==color,
                    "Unscoped native billboard changed");
                Require(ModHud.Place(billboard,origin,forward,true),"Known HUD rejected");
                double depth=Vector3D.Dot(billboard.Position0-origin,forward);
                Require(Math.Abs(depth-10)<.00001 && Vector3D.Cross(a-origin,billboard.Position0-origin).Length()<.00001,"Known HUD left its view ray/depth");
                if(billboard is MyTriangleBillboard) Require(billboard.Position3==d,"Triangle's unused fourth vertex changed");
            }
            var hidden=Quad(origin);
            Require(!ModHud.Place(hidden,origin,forward,false),"Hidden known HUD survived");
            var world=Quad(origin); world.Position0+=forward;
            var position=world.Position1;
            Require(ModHud.Place(world,origin,forward,false) && world.Position1==position,"Distant framework world geometry changed");
            var custom=Quad(origin); custom.CustomViewProjection=2; position=custom.Position0;
            Require(ModHud.Place(custom,origin,forward,false) && custom.Position0==position,"Framework custom-view billboard changed");
            var field=AccessTools.Field(typeof(ModHud),"drawing");
            int previous=(int)field.GetValue(null);
            try
            {
                var error=new InvalidOperationException("fixture"); field.SetValue(null,2);
                var result=AccessTools.Method(typeof(ModHud),"End").Invoke(null,new object[] {error,0});
                Require(ReferenceEquals(result,error) && (int)field.GetValue(null)==0,"Exceptional mod draw leaked HUD provenance");
            }
            finally { field.SetValue(null,previous); }
            log("PASS mod HUD provenance: unscoped native quads/triangles unchanged, known HUD view rays/depth, hidden HUD, distant/custom views and exception cleanup");
        }
        private static void Registration(Action<string> log)
        {
            var assembly=AppDomain.CurrentDomain.DefineDynamicAssembly(new AssemblyName("SEVR.HudContract.Tests"),AssemblyBuilderAccess.Run);
            var module=assembly.DefineDynamicModule("Contracts");
            var types=new List<Type>();
            bool accessException=false;
            EventHandler<FirstChanceExceptionEventArgs> observe=(sender,args)=> { if(args.Exception is MemberAccessException) accessException=true; };
            var attempted=(ISet<Type>)AccessTools.Field(typeof(ModHud),"attempted").GetValue(null);
            var pools=(IDictionary)AccessTools.Field(typeof(ModHud),"richPools").GetValue(null);
            AppDomain.CurrentDomain.FirstChanceException+=observe;
            try
            {
                for(int shape=0;shape<3;shape++)
                {
                    var builder=module.DefineType("Billboards"+shape,TypeAttributes.Public);
                    foreach(string field in new[] {"triangleList","flatTriangleList"})
                        builder.DefineField(field,typeof(List<object>),FieldAttributes.Private);
                    builder.DefineField("triPoolBack",shape==2 ? typeof(object):typeof(List<MyTriangleBillboard>[]),FieldAttributes.Private);
                    if(shape!=1) builder.DefineField("flatTriPoolBack",typeof(List<MyTriangleBillboard>[]),FieldAttributes.Private);
                    if(shape!=0) builder.DefineMethod("UpdateBillboards",MethodAttributes.Private,typeof(void),Type.EmptyTypes).GetILGenerator().Emit(OpCodes.Ret);
                    var type=builder.CreateType(); types.Add(type);
                    AccessTools.Method(typeof(ModHud),"AttachRich").Invoke(null,new object[] {type});
                    Require(attempted.Contains(type) && !pools.Contains(type),"Unsupported Rich HUD contract registered");
                }
                var root=module.DefineType("TextRoot",TypeAttributes.Public).CreateType(); types.Add(root);
                AccessTools.Method(typeof(ModHud),"AttachText").Invoke(null,new object[] {assembly,root});
                Require(attempted.Contains(root),"Unsupported Text HUD contract was retried");
                Require(!accessException,"Optional HUD discovery raised an exception that disables the plugin in Pulsar");
            }
            finally
            {
                AppDomain.CurrentDomain.FirstChanceException-=observe;
                foreach(var type in types) attempted.Remove(type);
            }
            log("PASS mod HUD discovery: client copies, missing/wrong pool fields and unsupported Text HUD skipped without first-chance member exceptions");
        }
        private static MyBillboard Quad(Vector3D origin) => new MyBillboard {
            CustomViewProjection=-1,Color=Vector4.One,
            Position0=origin+new Vector3D(-.01,-.01,-.1), Position1=origin+new Vector3D(.01,-.01,-.1),
            Position2=origin+new Vector3D(.01,.01,-.1), Position3=origin+new Vector3D(-.01,.01,-.1) };
        private static MyTriangleBillboard Triangle(Vector3D origin) => new MyTriangleBillboard {
            CustomViewProjection=-1,Color=Vector4.One,
            Position0=origin+new Vector3D(-.01,-.01,-.1),Position1=origin+new Vector3D(.01,-.01,-.1),Position2=origin+new Vector3D(.01,.01,-.1) };
        private static void Require(bool condition,string message) { if(!condition) throw new Exception(message); }
    }
}
