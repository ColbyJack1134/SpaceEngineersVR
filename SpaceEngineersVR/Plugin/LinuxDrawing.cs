#if LINUX
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SpaceEngineersVR.Plugin
{
    // Pulsar stages the generic System.Drawing.Common, which throws outside Windows; the Unix build from its NuGet cache must bind before any System.Drawing type loads.
    internal static class LinuxDrawing
    {
        private const string Name="System.Drawing.Common";
        // Only 6.0 has the Unix support switch; the hub descriptor pins that version.
        private const string UnixBuild="NuGet/system.drawing.common/6.0.0/runtimes/unix/lib/net6.0/System.Drawing.Common.dll";
        private static bool loaded;

        internal static void Load(string folder,Action<string> log)
        {
            if(loaded) return;
            if(!NativeLibrary.TryLoad("libgdiplus.so",out _)&&!NativeLibrary.TryLoad("libgdiplus.so.0",out _))
                throw new DllNotFoundException("libgdiplus is missing. Install the libgdiplus package from your distribution.");
            AppContext.SetSwitch("System.Drawing.EnableUnixSupport",true);
            var drawing=Bind(folder,log);
            loaded=true;
            log(Name+" "+drawing.GetName().Version+" from "+drawing.Location);
            Probe(log);
        }

        private static Assembly Bind(string folder,Action<string> log)
        {
            var current=AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name==Name);
            if(current!=null)
            {
                log(Name+" was already loaded before the plugin started");
                return current;
            }
            var unix=UnixBuildPath(folder);
            if(unix!=null) return Assembly.LoadFrom(unix);
            log("Unix build of "+Name+" not found above "+folder+"; using the staged copy");
            return Assembly.Load(Name);
        }

        // The plugin's Bin folder sits under Pulsar's data folder, which also holds the NuGet cache.
        internal static string UnixBuildPath(string folder)
        {
            for(var dir=new DirectoryInfo(folder);dir!=null;dir=dir.Parent)
            {
                var path=Path.Combine(dir.FullName,UnixBuild);
                if(File.Exists(path)) return path;
            }
            return null;
        }

        // Separate method so System.Drawing binds only after Bind chose the assembly.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Probe(Action<string> log)
        {
            using(var bitmap=new System.Drawing.Bitmap(4,4))
            using(var graphics=System.Drawing.Graphics.FromImage(bitmap))
            using(var font=new System.Drawing.Font("Segoe UI",12f))
            {
                graphics.DrawString("VR",font,System.Drawing.Brushes.White,0f,0f);
                log("System.Drawing renders; Segoe UI resolves to "+font.FontFamily.Name);
            }
        }
    }
}
#endif
