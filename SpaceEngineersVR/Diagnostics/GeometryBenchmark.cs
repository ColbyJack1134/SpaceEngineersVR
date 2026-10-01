using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using SpaceEngineersVR.Player;

namespace SpaceEngineersVR.Diagnostics
{
    public static class GeometryBenchmark
    {
        public static void Run(string game,Action<string> log)
        {
            string content=Path.GetFullPath(Path.Combine(game,"..","Content"));
            for(int i=0;i<6;i++)
            {
                int collections=GC.CollectionCount(0);
                var clock=Stopwatch.StartNew();
                var geometry=CockpitGeometry.Load(content);
                clock.Stop();
                log($"Geometry load {i}: {clock.Elapsed.TotalMilliseconds:F3} ms; gen0 {GC.CollectionCount(0)-collections}");
                if(i!=0) continue;
                using(var bytes=new MemoryStream())
                using(var writer=new BinaryWriter(bytes))
                using(var sha=SHA256.Create())
                {
                    foreach(var part in geometry.Parts)
                    {
                        writer.Write(part.Indices.Count);
                        foreach(int index in part.Indices) writer.Write(index);
                        foreach(var v in part.Positions) { writer.Write(v.X); writer.Write(v.Y); writer.Write(v.Z); }
                        foreach(var v in part.Normals) { writer.Write(v.X); writer.Write(v.Y); writer.Write(v.Z); }
                        foreach(var v in part.Tangents) { writer.Write(v.X); writer.Write(v.Y); writer.Write(v.Z); }
                        foreach(var v in part.TexCoords) { writer.Write(v.X); writer.Write(v.Y); }
                        foreach(var section in part.Sections) { writer.Write(section.IndexStart); writer.Write(section.TriCount); writer.Write(section.MaterialName); }
                    }
                    writer.Flush(); bytes.Position=0;
                    log("Geometry SHA256: "+BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-",""));
                }
            }
            log("First call includes JIT and cold managed state; later calls can reuse the OS file cache. No world or renderer was started.");
        }
    }
}
