using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using SpaceEngineersVR.Player;
using VRage.FileSystem;
using VRage.Utils;
using VRageMath;
using VRageRender;
using VRageRender.Messages;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class CockpitMaterialTests
    {
        private static readonly List<Dictionary<MyStringId,MyTextureChange>> textures=new List<Dictionary<MyStringId,MyTextureChange>>();
        private static readonly List<RenderFlags[]> flags=new List<RenderFlags[]>();
        private static bool Texture(Dictionary<MyStringId,MyTextureChange> textureChanges)
        { textures.Add(textureChanges); return false; }
        private static bool Properties(RenderFlags addFlags,RenderFlags removeFlags)
        { flags.Add(new[] {addFlags,removeFlags}); return false; }
        internal static void Run(Action<string> log)
        {
            Bindings(log);
            Skins(log);
        }
        private static void Bindings(Action<string> log)
        {
            var type=AccessTools.TypeByName("VRageRender.MyMeshMaterials1");
            var convert=AccessTools.Method(type,"ConvertImportDescToMeshMaterialInfo");
            var register=type.GetMethods(BindingFlags.Static|BindingFlags.NonPublic).Single(m=>m.Name=="GetMaterialId" && m.GetParameters()[0].ParameterType==convert.ReturnType.MakeByRefType());
            var lookup=AccessTools.Method(type,"GetMaterialId",new[] {typeof(string)});
            var names=AccessTools.Field(type,"MaterialNameIndex");
            var table=(IDictionary)names.GetValue(null);
            var saved=table.Keys.Cast<object>().ToDictionary(k=>k,k=>table[k]);
            try
            {
                var cab=CockpitRig.Find("SmallBlockCapCockpit");
                var source=cab.Geometry(MyFileSystem.ContentPath).Materials["PaintedMetal_Darker"];
                var native=convert.Invoke(null,new object[] {source,MyFileSystem.ContentPath,null});
                var nativeId=register.Invoke(null,new[] {native,null});
                foreach(var rig in CockpitRig.All)
                {
                    var geometry=rig.Geometry(MyFileSystem.ContentPath);
                    Require(geometry.Parts.SelectMany(p=>p.Sections).All(s=>geometry.Materials.ContainsKey(s.MaterialName)),"Cockpit replacement lacks a source material: "+rig.Subtype);
                    var message=new MyRenderMessageAddRuntimeModel {Name="SEVR_MaterialTest_"+rig.Subtype,ModelData=new MyModelData()};
                    foreach(var section in geometry.Parts.SelectMany(p=>p.Sections))
                    {
                        var copy=section; copy.MaterialName=CockpitMaterials.Name(rig.Subtype,section.MaterialName);
                        message.ModelData.Sections.Add(copy);
                    }
                    CockpitMaterials.Prepare(message.Name,rig.Subtype,geometry);
                    CockpitMaterials.Register(message);
                    foreach(var section in message.ModelData.Sections) Require(table.Contains(MyStringId.GetOrCompute(section.MaterialName)),"Runtime material was not registered: "+section.MaterialName);
                }
                string alias=CockpitMaterials.Name(cab.Subtype,source.MaterialName);
                CockpitMaterials.Register(alias,source);
                var baseline=lookup.Invoke(null,new object[] {alias});
                var rust=convert.Invoke(null,new object[] {source,MyFileSystem.ContentPath,null});
                AccessTools.Field(convert.ReturnType,"Name").SetValue(rust,MyStringId.GetOrCompute(alias));
                AccessTools.Field(convert.ReturnType,"ColorMetal_Texture").SetValue(rust,"Textures/Models/Cubes/armor/Skins/HeavyRust/RustColorable_cm.DDS");
                register.Invoke(null,new[] {rust,null});
                Require(!baseline.Equals(lookup.Invoke(null,new object[] {alias})),"Native material cache did not reproduce skin contamination");
                CockpitMaterials.Register(alias,source);
                Require(baseline.Equals(lookup.Invoke(null,new object[] {alias})),"Cached base material did not replace a stale skin name binding");
                string saddle=CockpitMaterials.Name("SpeederCockpit",source.MaterialName);
                CockpitMaterials.Register(saddle,source);
                Require(!baseline.Equals(lookup.Invoke(null,new object[] {saddle})),"Cockpit types share a replacement material identity");
                Require(nativeId.Equals(lookup.Invoke(null,new object[] {source.MaterialName})),"Replacement registration changed the native cockpit material");
                foreach(var pair in saved) Require(table.Contains(pair.Key) && pair.Value.Equals(table[pair.Key]),"Private cockpit material registration changed a native material binding");
            }
            finally
            {
                table.Clear(); foreach(var pair in saved) table.Add(pair.Key,pair.Value);
            }
            log("PASS cockpit materials: all installed rig/template sources, native rust-cache reproduction, cached clean binding restored and private cockpit identities");
        }
        private static void Skins(Action<string> log)
        {
            var harmony=new Harmony("SEVR.CockpitMaterial.Tests");
            var texture=AccessTools.Method(typeof(MyRenderProxy),nameof(MyRenderProxy.ChangeMaterialTexture),new[] {typeof(uint),typeof(Dictionary<MyStringId,MyTextureChange>)});
            var properties=AccessTools.Method(typeof(MyRenderProxy),nameof(MyRenderProxy.UpdateModelProperties));
            var state=new[] {"verification","geometry","activeRig","appliedSkin"}.ToDictionary(n=>n,n=>AccessTools.Field(typeof(CockpitRender),n).GetValue(null));
            var screens=(IDictionary)AccessTools.Field(typeof(CockpitRender),"screenTextures").GetValue(null);
            var savedScreens=screens.Keys.Cast<object>().ToDictionary(k=>k,k=>screens[k]);
            try
            {
                harmony.Patch(texture,prefix:new HarmonyMethod(typeof(CockpitMaterialTests),nameof(Texture)));
                harmony.Patch(properties,prefix:new HarmonyMethod(typeof(CockpitMaterialTests),nameof(Properties)));
                var rig=CockpitRig.Find("SmallBlockCapCockpit"); var geometry=rig.Geometry(MyFileSystem.ContentPath);
                var check=new CockpitRender.Verification(1,geometry,Array.Empty<string>(),rig.Subtype) {Actors=Enumerable.Repeat(uint.MaxValue,geometry.Parts.Length).ToArray()};
                check.Actors[0]=42;
                AccessTools.Field(typeof(CockpitRender),"verification").SetValue(null,check);
                AccessTools.Field(typeof(CockpitRender),"geometry").SetValue(null,geometry);
                AccessTools.Field(typeof(CockpitRender),"activeRig").SetValue(null,rig);
                AccessTools.Field(typeof(CockpitRender),"appliedSkin").SetValue(null,null);
                var changes=new Dictionary<MyStringId,MyTextureChange> {
                    [MyStringId.GetOrCompute("PaintedMetal_Darker")]=new MyTextureChange {ColorMetalFileName="rust_cm",NormalGlossFileName="rust_ng",ExtensionsFileName="rust_add",AlphamaskFileName="rust_alpha"},
                    [MyStringId.GetOrCompute("UnrelatedMaterial")]=new MyTextureChange {ColorMetalFileName="unrelated"} };
                textures.Clear(); flags.Clear(); screens["CockpitScreen_01"]="live-screen";
                CockpitRender.SyncSkin(MyStringHash.GetOrCompute("Rust"),changes,false);
                var mapped=textures[1][MyStringId.GetOrCompute(CockpitMaterials.Name(rig.Subtype,"PaintedMetal_Darker"))];
                Require(textures.Count==2 && textures[0]==null && textures[1].Count==1 && mapped.ColorMetalFileName=="rust_cm" &&
                    mapped.NormalGlossFileName=="rust_ng" && mapped.ExtensionsFileName=="rust_add" && mapped.AlphamaskFileName=="rust_alpha" &&
                    screens.Count==0 && flags.All(f=>f[0]==0 && f[1]==RenderFlags.MetalnessColorable),"Skin was not mapped to replacement materials or invalidated LCD overrides");
                CockpitRender.SyncSkin(MyStringHash.GetOrCompute("Rust"),changes,false);
                Require(textures.Count==2,"Unchanged skin was reapplied each frame");
                flags.Clear();
                CockpitRender.SyncSkin(MyStringHash.GetOrCompute("Gold"),changes,true);
                Require(textures.Count==4 && textures[2]==null && flags.Count>0 && flags.All(f=>f[0]==RenderFlags.MetalnessColorable && f[1]==0),"Skin replacement did not clear prior overrides or preserve metalness coloring");
                CockpitRender.SyncSkin(MyStringHash.NullOrEmpty,null,false);
                Require(textures.Count==5 && textures[4]==null,"Removing a skin retained old texture overrides");
            }
            finally
            {
                harmony.Unpatch(texture,HarmonyPatchType.All,harmony.Id); harmony.Unpatch(properties,HarmonyPatchType.All,harmony.Id);
                foreach(var pair in state) AccessTools.Field(typeof(CockpitRender),pair.Key).SetValue(null,pair.Value);
                screens.Clear(); foreach(var pair in savedScreens) screens.Add(pair.Key,pair.Value);
                textures.Clear(); flags.Clear();
            }
            log("PASS cockpit skin updates: actor material aliases, all texture channels, change/removal, metallic flags, no repeated updates and LCD resynchronization");
        }
        private static void Require(bool value,string message) { if(!value) throw new Exception(message); }
    }
}
