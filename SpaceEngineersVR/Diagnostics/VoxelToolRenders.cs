#if SEVR_DIAGNOSTICS
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SharpDX.Direct3D11;
using SpaceEngineersVR.Player;
using VRage;
using VRage.FileSystem;
using VRage.Utils;
using VRageMath;
using Device = SharpDX.Direct3D11.Device;

namespace SpaceEngineersVR.Diagnostics
{
    // Production wheel and HUD painters with installed voxel material data; definitions are not loaded here, so labels and icons come from the same files.
    internal static class VoxelToolRenders
    {
        internal static void Run(Device device,string output,Action<string> log)
        {
            var radial=XDocument.Load(Path.Combine(MyFileSystem.ContentPath,"Data","RadialMenu.sbc")).Descendants("RadialMenu")
                .First(d=>(string)d.Element("Id")?.Attribute("Subtype")=="VoxelHand");
            var sections=radial.Descendants("Section").Select(section=>new {
                Title=MyTexts.GetString(MyStringId.GetOrCompute((string)section.Element("Label"))),
                Items=section.Descendants("Item").Select(item=>new ActionChoice(MyTexts.GetString(MyStringId.GetOrCompute((string)item.Element("LabelName"))),()=> {},
                    icon:(string)item.Element("Icons")?.Elements("string").FirstOrDefault())).ToArray()}).ToArray();
            var materials=sections.Select(section=>new ToolbarWheel.Group(section.Title,section.Items,true)).ToArray();
            var previews=Directory.GetFiles(Path.Combine(MyFileSystem.ContentPath,"Data"),"VoxelMaterials*.sbc").SelectMany(f=>XDocument.Load(f).Descendants("VoxelMaterial"))
                .Select(m=>new {Id=(string)m.Element("Id")?.Element("SubtypeId"),Icon=(string)m.Element("VoxelHandPreview")})
                .Where(m=>m.Id!=null && m.Icon!=null).GroupBy(m=>m.Id).ToDictionary(g=>g.Key,g=>g.First().Icon);
            foreach(var item in radial.Descendants("Item"))
            {
                string id=(string)item.Element("Material")?.Element("SubtypeId"),icon=(string)item.Element("Icons")?.Elements("string").FirstOrDefault();
                if(id!=null && icon!=null) previews[id]=icon;
            }
            string mod=Path.GetFullPath(Path.Combine(MyFileSystem.ContentPath,"..","..","..","workshop","content","244850","2820001381","Data","Scripts","Terrain","TerrainToolMod.cs"));
            string[] terrain=File.Exists(mod) ? Regex.Matches(Regex.Match(File.ReadAllText(mod),@"TERRAIN_NAMES = new string\[NUMBER_OF_TERRAIN_TYPES\] \{([^}]*)\}").Groups[1].Value,"\"([^\"]+)\"")
                .Cast<Match>().Select(m=>m.Groups[1].Value).ToArray() : new string[0];
            var terrainChoices=terrain.Select(name=>new ActionChoice(VoxelTools.Words(name),()=> {},icon:previews.TryGetValue(name,out var icon) ? icon:null)).ToArray();
            var quick=GameActions.WheelActions(false,false,false);
            using(var wheel=new OverlayCanvas("Voxel wheel",1024,1024,1,false,device))
            {
                Wheel(wheel,ToolbarWheel.ToolGroups(VoxelTools.Hand.Title,VoxelTools.Hand.Actions(),materials,quick),new[] {0,1,2,materials.Length+1},output,"wheel-voxel-");
                var digi=VoxelToolTests.Fixture();
                if(terrainChoices.Length>0)
                    Wheel(wheel,ToolbarWheel.ToolGroups("Terrain tool",digi.Actions(),new[] {new ToolbarWheel.Group("Soil and stone",terrainChoices.Take(42).ToArray(),true),
                        new ToolbarWheel.Group("Snow and ice",terrainChoices.Skip(42).ToArray(),true)},quick),new[] {0,1,6},output,"wheel-terrain-");
            }
            using(var hud=new OverlayCanvas("Voxel HUD",1280,560,1,false,device))
            {
                var all=sections.SelectMany(s=>s.Items).ToArray();
                var stone=all.FirstOrDefault(i=>i.Label.StartsWith("Stone",StringComparison.OrdinalIgnoreCase)) ?? all[0];
                var model=new EssentialHud.View {
                    Levels=new[] { .72f,.46f,.88f,.19f },Values=new[] { "72","46","88","19" },Icons=new[] {stone.Icon},Selected=stone.Label,Ammo="",
                    Helmet=true,Jetpack=true,Flying=false,Broadcasting=true,Flashlight=false,OxygenBottles="2",HydrogenBottles="3",EnvironmentOxygen="High",Temperature="Warm",
                    Speed="0.00",SpeedLevel=0,NaturalGravity="1.00",ArtificialGravity="0.00",Down=new Vector3(0,-1,0) };
                UiTests.Render(hud,()=>EssentialHud.Paint(hud,model)); UiTests.Save(hud.Texture,Path.Combine(output,"hud-voxel-material.png"));
                if(terrainChoices.Length>2)
                {
                    model.Selected=terrainChoices[2].Label; model.Icons=new[] {terrainChoices[2].Icon}; model.Ammo="38";
                    UiTests.Render(hud,()=>EssentialHud.Paint(hud,model)); UiTests.Save(hud.Texture,Path.Combine(output,"hud-terrain-tool.png"));
                }
            }
            log("PASS voxel tool previews: "+materials.Sum(m=>m.Items.Length)+" native materials in "+materials.Length+" groups, "+terrain.Length+" terrain types");
        }
        // Renders selected pages of a Y wheel built by the production layout, with the same hint as the live wheel.
        internal static void Wheel(OverlayCanvas wheel,ToolbarWheel.Group[] groups,int[] pages,string output,string prefix)
        {
            var layout=ToolbarWheel.WithRecents(ToolbarWheel.Layout(groups,out var titles,out var variants),new ActionChoice[0]);
            foreach(int page in pages.Where(p=>p<titles.Length))
            {
                var items=layout[page];
                var model=new ToolbarWheel.View {Group=1,SubIcons=new string[9],ItemText=new string[9],Hint=ToolbarWheel.QuickControlsHint(true),
                    Title=titles[page],Variants=variants[page],Page=page,Pages=layout.Length,Selected=variants[page] ? 2:-1,
                    Labels=items.Select(c=>c?.Label ?? "").ToArray(),Icons=items.Select(c=>c?.Icon==null ? new string[0]:new[] {c.Icon}).ToArray(),
                    Enabled=items.Select(c=>c!=null).ToArray()};
                UiTests.Render(wheel,()=>ToolbarWheel.Paint(wheel,model)); UiTests.Save(wheel.Texture,Path.Combine(output,prefix+page+".png"));
            }
        }
    }
}
#endif
