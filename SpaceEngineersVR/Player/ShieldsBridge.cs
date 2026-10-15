using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using SpaceEngineersVR.Plugin;
using SpaceEngineersVR.Util;
using VRage;
using VRage.Game.Entity;
using VRage.ModAPI;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    // Read-only client of the Defense Shields mod API. Nothing here does anything unless Defense Shields answers.
    internal static class ShieldsBridge
    {
        private static readonly ModApiClient api=new ModApiClient("Defense Shields",1365616918,Attach,Detach,ignoreImmutable:true);
        private static Func<MyEntity,MyTuple<bool,bool,float,float,float,int>> shieldInfo;
        private static Func<MyEntity,MyTuple<bool,bool,float,float>> modulationInfo;
        private static Func<IMyEntity,IMyTerminalBlock> shieldBlock;
        private static Func<IMyTerminalBlock,string> shieldStatus;
        private static Func<MyEntity,MyTuple<bool,Vector3I>> facesFast;
        // The live Defense Shields session, for per-face state its API does not expose; looked up at most once a second.
        private static Func<object> session;
        private static DateTime sessionRetry;
        private static bool facesFailed;

        internal static bool Ready => api.Ready;
        internal static void Load() => api.Load();
        internal static void Unload() => api.Unload();

        internal struct Info
        {
            public bool Exists,Online;
            public float Charge,MaxCharge,Percent;
            public int Heat;
        }

        private static bool Attach(IReadOnlyDictionary<string,Delegate> methods)
        {
            shieldInfo=ModApiClient.Get<Func<MyEntity,MyTuple<bool,bool,float,float,float,int>>>(methods,"GetShieldInfo");
            modulationInfo=ModApiClient.Get<Func<MyEntity,MyTuple<bool,bool,float,float>>>(methods,"GetModulationInfo");
            shieldBlock=ModApiClient.Get<Func<IMyEntity,IMyTerminalBlock>>(methods,"GetShieldBlock");
            shieldStatus=ModApiClient.Get<Func<IMyTerminalBlock,string>>(methods,"ShieldStatus");
            facesFast=ModApiClient.Get<Func<MyEntity,MyTuple<bool,Vector3I>>>(methods,"GetFacesFast");
            return shieldInfo!=null;
        }

        private static void Detach()
        {
            shieldInfo=null; modulationInfo=null; shieldBlock=null; shieldStatus=null; facesFast=null;
            session=null; sessionRetry=DateTime.MinValue; facesFailed=false;
        }

        // Shield state of the grid (any grid in a shielded construct resolves to its shield).
        internal static Info Of(MyEntity grid)
        {
            if(!Ready || grid==null) return default(Info);
            var raw=shieldInfo(grid.GetTopMostParent());
            return new Info {Exists=raw.Item1,Online=raw.Item2,Charge=raw.Item3,MaxCharge=raw.Item4,Percent=raw.Item5,Heat=raw.Item6};
        }

        // Kinetic and energy modulation (1 = neutral) and whether reinforce / EMP enhancers are enabled.
        internal static MyTuple<bool,bool,float,float>? Modulation(MyEntity grid) =>
            Ready && modulationInfo!=null && grid!=null ? modulationInfo(grid.GetTopMostParent()) : (MyTuple<bool,bool,float,float>?)null;

        // Side shunting: null when side shunting is off. Per axis -1/1 picks one face, 2 both (Defense Shields' IsSideShunted).
        internal static Vector3I? Shunting(MyEntity grid)
        {
            if(!Ready || grid==null || facesFast==null) return null;
            var result=facesFast(grid.GetTopMostParent());
            return result.Item1 ? result.Item2 : (Vector3I?)null;
        }

        // Faces in Defense Shields order: Forward, Backward, Left, Right, Up, Down.
        internal static readonly string[] FaceNames={"Front","Back","Left","Right","Up","Down"};
        internal static bool FaceShunted(Vector3I state,int face)
        {
            switch(face)
            {
                case 2: return state.X==-1 || state.X==2;
                case 3: return state.X==1 || state.X==2;
                case 4: return state.Y==1 || state.Y==2;
                case 5: return state.Y==-1 || state.Y==2;
                case 0: return state.Z==-1 || state.Z==2;
                default: return state.Z==1 || state.Z==2;
            }
        }

        // Per-face health (0..1, or -1 when this client has no charge base) and online state, read from the mod's synced shield state as its own HUD does. Null when unavailable.
        internal static bool TryFaces(MyEntity grid,out float[] health,out bool[] online)
        {
            health=null; online=null;
            if(!Ready || grid==null) return false;
            try
            {
                if(session==null && DateTime.UtcNow>=sessionRetry)
                {
                    sessionRetry=DateTime.UtcNow.AddSeconds(1);
                    var type=ModReflection.LiveType("DefenseShields.Session","Instance");
                    session=type==null ? null : ModReflection.StaticReader(type,"Instance");
                }
                if(!(ModReflection.Get(session?.Invoke(),"IdToBus") is System.Collections.IDictionary bus)) return false;
                long id=grid.GetTopMostParent().EntityId;
                if(!bus.Contains(id)) return false;
                var logic=ModReflection.Get(bus[id],"DefenseShields");
                var sides=ModReflection.Path(logic,"DsState","State","ShieldSides") as Array;
                float total=ModReflection.Value(logic,0f,"ShieldChargeBase");
                if(sides==null || sides.Length!=6) return false;
                health=new float[6]; online=new bool[6];
                float max=total/6;
                for(int i=0;i<6;i++)
                {
                    var side=sides.GetValue(i);
                    online[i]=ModReflection.Value(side,false,"Online");
                    var charge=ModReflection.Get(side,"Charge");
                    double value=charge==null ? 0 : Convert.ToDouble(charge);
                    health[i]=total>0 ? (float)Math.Max(0,Math.Min(1,value/max)) : -1;
                }
                return true;
            }
            catch(Exception ex)
            {
                if(!facesFailed) { facesFailed=true; Logger.Warning(ex,"Defense Shields face read failed; the Shields page shows totals only"); }
                return false;
            }
        }

        internal static string Status(MyEntity grid)
        {
            if(!Ready || grid==null || shieldBlock==null || shieldStatus==null) return null;
            var block=shieldBlock(grid);
            return block==null ? null : shieldStatus(block);
        }
    }
}
