#if LINUX
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
#endif

namespace SpaceEngineersVR.Util
{
    // linux-compat translates only MyFileSystem reads; direct File reads resolve Windows separators and casing here.
    internal static class NativePath
    {
#if LINUX
        private static readonly ConcurrentDictionary<string,string[]> listings=new ConcurrentDictionary<string,string[]>();

        internal static string Of(string path)
        {
            if(string.IsNullOrEmpty(path)) return path;
            path=path.Replace('\\','/');
            if(File.Exists(path) || Directory.Exists(path)) return path;
            string[] parts=path.Split('/');
            string current=path.StartsWith("/") ? "/" : ".";
            for(int i=0;i<parts.Length;i++)
            {
                if(parts[i].Length==0 || parts[i]==".") continue;
                string exact=Path.Combine(current,parts[i]);
                if(parts[i]==".." || File.Exists(exact) || Directory.Exists(exact)) { current=exact; continue; }
                string match=Directory.Exists(current) ? listings.GetOrAdd(current,d => Directory.GetFileSystemEntries(d).Select(Path.GetFileName).ToArray())
                    .FirstOrDefault(n => string.Equals(n,parts[i],StringComparison.OrdinalIgnoreCase)) : null;
                if(match==null) return path;
                current=Path.Combine(current,match);
            }
            return current;
        }
#else
        internal static string Of(string path) => path;
#endif
    }
}
