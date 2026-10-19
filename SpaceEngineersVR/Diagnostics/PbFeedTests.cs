#if SEVR_DIAGNOSTICS
using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using SpaceEngineersVR.Plugin;
using VRage.Scripting;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class PbFeedTests
    {
        public static void Run(Action<string> log)
        {
            var message=new PbMessage {Controller=-1234567890123,Flags=PbMessage.Arms|PbMessage.Head|PbMessage.Left};
            message.HeadPose=Matrix.CreateFromYawPitchRoll(.4f,-.2f,.1f); message.HeadPose.Translation=new Vector3(.05f,-.1f,.02f);
            message.LeftHand.Pose=Matrix.CreateFromYawPitchRoll(-1.2f,.6f,2.5f); message.LeftHand.Pose.Translation=new Vector3(-.25f,-.35f,-.4f);
            message.LeftHand.Trigger=.5f; message.LeftHand.Grip=1; message.LeftHand.Stick=new Vector2(-.75f,.25f);
            message.LeftHand.Curls[0]=.5f; message.LeftHand.Curls[4]=.125f;
            message.RightHand.Trigger=float.NaN;

            string text;
            var culture=Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture=new CultureInfo("de-DE");
                text=PbMessage.Format(7,message.Body());
            }
            finally { Thread.CurrentThread.CurrentCulture=culture; }
            string[] fields=text.Split(' ');
            if(fields.Length!=PbMessage.Fields || fields[0]!=PbMessage.Tag || text.Contains(",") || text.Contains("NaN"))
                throw new Exception("VR data message layout: "+text);
            if(text.Length>400) throw new Exception("VR data message is "+text.Length+" characters");
            // The example script stays local (ignored IngameScripts/); clean checkouts check the layout only.
            if(Assembly.GetEntryAssembly().GetManifestResourceInfo("SevrVrData.cs")==null)
            {
                log("PASS programmable block VR data: invariant layout (example script not present, parse check skipped)");
                return;
            }

            var data=Parser();
            if(!(bool)Call(data,"TryParse",text)) throw new Exception("Example script rejected: "+text);
            Expect(Get(data,"Sequence"),7u,"sequence"); Expect(Get(data,"Controller"),-1234567890123L,"controller"); Expect(Get(data,"Flags"),message.Flags,"flags");
            Pose((MatrixD)Get(data,"HeadPose"),message.HeadPose,"head");
            object left=Get(data,"LeftHand"),right=Get(data,"RightHand");
            Pose((MatrixD)Get(left,"Pose"),message.LeftHand.Pose,"left hand");
            Expect(Get(left,"Trigger"),.5f,"left trigger"); Expect(Get(left,"Grip"),1f,"left grip"); Expect(Get(left,"Stick"),new Vector2(-.75f,.25f),"left stick");
            var curls=(float[])Get(left,"Curls");
            if(Math.Abs(curls[0]-.5f)>.001f || Math.Abs(curls[4]-.125f)>.001f || curls[1]!=0) throw new Exception("Left curls: "+string.Join(" ",curls));
            Expect(Get(right,"Trigger"),0f,"untracked right trigger");

            string stop=PbMessage.Format(8,new PbMessage {Controller=5,Flags=PbMessage.Stopped}.Body());
            if(!(bool)Call(data,"TryParse",stop) || (int)Get(data,"Flags")!=PbMessage.Stopped) throw new Exception("Stop message not parsed: "+stop);
            foreach(string bad in new[] {null,"","run","SEVR2"+text.Substring(5),text.Substring(0,text.LastIndexOf(' ')),text+" 1",text.Replace(" 0.5 "," x ")})
                if((bool)Call(data,"TryParse",bad)) throw new Exception("Example script accepted: "+bad);
            Expect(Get(data,"Sequence"),8u,"sequence after rejected messages");
            log("PASS programmable block VR data: invariant layout, example script parses poses, inputs, curls and stop, rejects malformed arguments");
        }

        // Compiled exactly as a programmable block compiles its program (CompileIngameScriptAsync).
        private static object Parser()
        {
            var scripting=ModApiTests.Scripting();
            bool rejected=false;
            try { Compile(scripting,"public void Main() { System.IO.File.ReadAllText(\"x\"); }"); }
            catch(Exception ex) { rejected=ex.Message.Contains("is prohibited"); }
            if(!rejected) throw new Exception("Programmable block compiler accepted a call outside the whitelist; the example check proves nothing");
            var type=Compile(scripting,ModApiTests.Resource("SevrVrData.cs")).GetTypes().First(t => t.Name=="SevrData");
            return Activator.CreateInstance(type);
        }
        private static Assembly Compile(IVRageScripting scripting,string program)
        {
            var assembly=scripting.CompileIngameScriptAsync("SEVR.PbExampleTest",program,out var messages,"SEVR programmable block example","Program","MyGridProgram").Result;
            var errors=messages.Where(m => m.IsError).ToArray();
            if(errors.Length>0 || assembly==null) throw new Exception("Programmable block compiler errors: "+string.Join(" | ",errors.Select(m => m.Text)));
            return assembly;
        }

        private static void Pose(MatrixD actual,Matrix expected,string name)
        {
            if(Vector3D.Distance(actual.Translation,expected.Translation)>.0002 || Vector3D.Dot(actual.Forward,expected.Forward)<.9999 || Vector3D.Dot(actual.Up,expected.Up)<.9999)
                throw new Exception(name+" pose: "+actual+" expected "+expected);
        }
        private static void Expect(object actual,object expected,string name)
        {
            if(!Equals(actual,expected)) throw new Exception(name+": "+actual+" expected "+expected);
        }
        // Compiled scripts count instructions; outside a run block the counter throws (MyProgrammableBlock.ExecuteCode).
        private static object Call(object target,string method,string argument)
        {
            using(VRage.Library.Compiler.IlInjector.BeginRunBlock(50000,10000,false))
                return target.GetType().GetMethod(method).Invoke(target,new object[] {argument});
        }
        private static object Get(object target,string field) => target.GetType().GetField(field).GetValue(target);
    }
}
#endif
