using System;
using System.Linq;
using System.IO;
using VRage.ObjectBuilders;
using VRage.Utils;
using HarmonyLib;
using Sandbox.Definitions;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.World;
using SpaceEngineersVR.Player;
using VRage.Game.Utils;
using VRage.Game;
using VRageMath;
using VRageRender;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class BuildOrientationTests
    {
        private static void Require(bool value,string message) { if(!value) throw new Exception(message); }
        public static void Run(Action<string> log)
        {
            // The compositor layer has one head-relative plane and eye-correct
            // parallax. Verify its full extent fits both asymmetric eye frusta.
            foreach(int eye in new[] { -1,1 })
            {
                var projection=VrMath.Projection(eye<0 ? -1.1f : -.9f,eye<0 ? .9f : 1.1f,-1,1,.03);
                var view=MatrixD.CreateTranslation(-eye*.032,0,0);
                foreach(int x in new[] { -1,1 }) foreach(int y in new[] { -1,1 })
                {
                    var corner=Vector3D.Transform(new Vector3D(x*BuildOrientationHud.Width/2,y*BuildOrientationHud.Width/2,0),BuildOrientationHud.Mount);
                    var p=Vector4D.Transform(new Vector4D(corner,1),view*projection);
                    Require(p.W>0 && Math.Abs(p.X/p.W)<.8 && Math.Abs(p.Y/p.W)<.8,"Building HUD falls outside binocular central view");
                }
            }
            var now=DateTime.UtcNow; var sample=new BuildOrientationHud.View { Captured=now };
            foreach(InputMode mode in Enum.GetValues(typeof(InputMode)))
                Require(BuildOrientationHud.Visible(sample,now,true,mode)==(mode==InputMode.Building || mode==InputMode.Clipboard),
                    "Build orientation leaks outside placement context");
            Require(!BuildOrientationHud.Visible(sample,now,false,InputMode.Building) &&
                !BuildOrientationHud.Visible(sample,now.AddSeconds(1),true,InputMode.Building),"Hidden/stale building hint survived");
            log("PASS build orientation HUD: both asymmetric eye frusta, placement-only visibility and stale/hidden cleanup.");
        }
        internal static readonly System.Collections.Generic.List<BuildOrientationHud.View> Previews=new System.Collections.Generic.List<BuildOrientationHud.View>();
        private static void LoadMaterials()
        {
            // These normally load with a world; the isolated menu scene needs
            // the same installed definitions before native billboards are created.
            MyObjectBuilder_Definitions definitions;
            if(!MyObjectBuilderSerializer.DeserializeXML(Path.Combine(VRage.FileSystem.MyFileSystem.ContentPath,"Data","TransparentMaterials.sbc"),out definitions))
                throw new Exception("Native transparent material definitions unavailable");
            foreach(var d in definitions.TransparentMaterials.Where(d=>d.Id.SubtypeId=="SquareFullColor" || d.Id.SubtypeId.StartsWith("Arrow")))
                MyTransparentMaterials.AddMaterial(new MyTransparentMaterial(MyStringId.GetOrCompute(d.Id.SubtypeId),d.TextureType,d.Texture,d.GlossTexture,
                    d.SoftParticleDistanceScale,d.CanBeAffectedByOtherLights,d.AlphaMistingEnable,d.Color,d.ColorAdd,d.ShadowMultiplier,d.LightMultiplier,
                    d.IsFlareOccluder,d.TriangleFaceCulling,d.UseAtlas,d.AlphaMistingStart,d.AlphaMistingEnd,d.AlphaSaturation,d.Reflectivity,
                    d.AlphaCutout,d.TargetSize,d.Fresnel,d.ReflectionShadow,d.Gloss,d.GlossTextureAdd,d.SpecularColorFactor));
        }
        private static int[] Axes(MyBlockBuilderRotationHints hints) => new[] { hints.RotationRightAxis,hints.RotationRightDirection,
            hints.RotationUpAxis,hints.RotationUpDirection,hints.RotationForwardAxis,hints.RotationForwardDirection };
        internal static void RunNative(Action<string> log)
        {
            LoadMaterials();
            var property=AccessTools.Property(typeof(MySector),nameof(MySector.MainCamera));
            var old=MySector.MainCamera;
            // The main-menu fixture has not loaded a world's cube-size definitions.
            var definitions=AccessTools.Field(typeof(MyDefinitionManager),"m_definitions").GetValue(MyDefinitionManager.Static);
            var sizesField=AccessTools.Field(definitions.GetType(),"m_cubeSizes");
            var previousSizes=(float[])sizesField.GetValue(definitions);
            try
            {
                var sizes=(float[])previousSizes.Clone(); sizes[(int)MyCubeSize.Large]=2.5f; sizes[(int)MyCubeSize.Small]=.5f;
                sizesField.SetValue(definitions,sizes);
                var camera=new MyCamera(1f,new MyViewport(0,0,1920,1080));
                camera.SetViewMatrix(MatrixD.Identity,false); property.SetValue(null,camera,null);
                for(int i=0;i<12;i++)
                {
                    var matrix=MatrixD.CreateFromYawPitchRoll(i*.21,i*.13,-i*.07)*MatrixD.CreateTranslation(10,15,-20);
                    var drawn=new MyBlockBuilderRotationHints(); var hidden=new MyBlockBuilderRotationHints();
                    using(var capture=BuildOrientationHud.Begin(true))
                    {
                        drawn.CalculateRotationHints(matrix,true);
                        var preview=capture.Complete();
                        Require(preview!=null && preview.Sprites.Length>=8,"Native rotation cube/arrows were not captured");
                        Require(preview.Sprites.All(s=>!s.Path.EndsWith("FAKE.dds",StringComparison.OrdinalIgnoreCase)) &&
                            preview.Sprites.Select(s=>s.Path).Distinct().Count()>=4,"Rotation preview uses missing/placeholder materials");
                        foreach(var sprite in preview.Sprites)
                        foreach(var p in new[] {sprite.TopLeft,sprite.TopRight,sprite.BottomLeft,sprite.BottomRight})
                            Require(p.W>0 && Math.Abs(p.X/p.W)<1 && Math.Abs(p.Y/p.W)<1,"Native rotation geometry clips its VR texture");
                        if(i<3) Previews.Add(preview);
                    }
                    hidden.CalculateRotationHints(matrix,false);
                    Require(Axes(drawn).SequenceEqual(Axes(hidden)) && hidden.RotationRightAxis>=0,
                        "Suppressing native drawing changed rotation axes/directions");
                    drawn.CalculateRotationHints(MatrixD.Identity,true,true,true);
                    hidden.CalculateRotationHints(MatrixD.Identity,false,true,true);
                    Require(Axes(drawn).SequenceEqual(Axes(hidden)),"Fixed/one-axis rotation changed when drawing suppressed");
                }
                log("PASS native rotation hints: captured native cube/arrow geometry stays inside VR texture; draw=false retains all six axis/direction results, including fixed/one-axis clipboard mode, across 12 native orientations. No world loaded.");
            }
            finally { sizesField.SetValue(definitions,previousSizes); property.SetValue(null,old,null); }
        }
    }
}
