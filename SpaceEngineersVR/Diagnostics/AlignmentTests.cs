using System;
using System.IO;
using System.Xml.Serialization;
using SpaceEngineersVR.Config;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class AlignmentTests
    {
        public static void Run(Action<string> log)
        {
            var offset=new AnchorOffsetSetting {Key="tool/test",X=.024f,Y=-.013f,Z=.05f,Pitch=12,Yaw=-31,Roll=7,Scale=1.2f};
            var world=MatrixD.CreateRotationY(1.3)*MatrixD.CreateTranslation(1e7,-2e7,3e7);
            var local=(MatrixD)offset.Matrix;
            var placed=local*world;
            if(Vector3D.Distance(placed.Translation,Vector3D.Transform(local.Translation,world))>1e-7 ||
                Math.Abs(placed.Right.Length()-1)>1e-6) throw new Exception("Alignment lost local-space translation or rigid orientation");
            var copy=offset.Copy(); copy.X=.3f;
            if(offset.X==copy.X) throw new Exception("Draft edit changed saved offset");
            foreach(float bad in new[] {float.NaN,float.PositiveInfinity,1.1f})
            { copy=offset.Copy(); copy.X=bad; if(copy.Valid) throw new Exception("Invalid anchor translation accepted"); }
            copy=offset.Copy(); copy.Scale=float.NaN;
            if(copy.Valid) throw new Exception("Invalid anchor scale accepted");

            string directory=Path.Combine(Path.GetTempPath(),"SEVR-settings-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string path=Path.Combine(directory,"settings.xml");
            try
            {
                var original=new PluginConfig {AnchorOffsets=new[] {offset},PlayerHeight=1.83f};
                using(var writer=new StreamWriter(path)) new XmlSerializer(typeof(PluginConfig)).Serialize(writer,original);
                using(var config=PersistentConfig<PluginConfig>.Load(path))
                {
                    config.Data.PlayerHeight=1.84f;
                    config.Data.AnchorOffsets=new[] {new AnchorOffsetSetting {Key="tool/Ω",X=-.04f,Roll=24}};
                    config.Data.StableShadows=false;
                    config.Data.PlayerHeight=1.85f;
                }
                using(var loaded=PersistentConfig<PluginConfig>.Load(path))
                {
                    if(loaded.Data.PlayerHeight!=1.85f || loaded.Data.StableShadows || loaded.Data.AnchorOffsets.Length!=1 ||
                        loaded.Data.AnchorOffsets[0].Key!="tool/Ω" || loaded.Data.AnchorOffsets[0].Roll!=24)
                        throw new Exception("Pending settings were lost or XML encoding changed during replacement");
                }
                if(File.Exists(path+".tmp")) throw new Exception("Configuration left an incomplete replacement");
            }
            finally { Directory.Delete(directory,true); }
            log("PASS alignment: local offsets at large coordinates, draft isolation, invalid input, XML replacement and pending changes flushed on dispose");
        }
    }
}
