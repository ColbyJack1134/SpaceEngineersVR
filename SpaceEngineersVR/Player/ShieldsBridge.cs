using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using SpaceEngineersVR.Plugin;
using VRage;
using VRage.Game.Entity;
using VRage.ModAPI;
using VRageMath;
using SpaceEngineersVR.Util;

namespace SpaceEngineersVR.Player
{
    // Read-only client of the Defense Shields mod API. Nothing here does anything unless Defense Shields answers.
    internal static class ShieldsBridge
    {
        private const long Channel=1365616918;
        private static bool registered;
        private static Func<MyEntity,MyTuple<bool,bool,float,float,float,int>> shieldInfo;
        private static Func<MyEntity,MyTuple<bool,bool,float,float>> modulationInfo;
        private static Func<IMyEntity,IMyTerminalBlock> shieldBlock;
        private static Func<IMyTerminalBlock,string> shieldStatus;
        private static Func<MyEntity,MyTuple<bool,Vector3I>> facesFast;

        internal static bool Ready { get; private set; }

        internal struct Info
        {
            public bool Exists,Online;
            public float Charge,MaxCharge,Percent;
            public int Heat;
        }

        internal static void Load()
        {
            if(registered || MyAPIGateway.Utilities==null) return;
            registered=true;
            MyAPIGateway.Utilities.RegisterMessageHandler(Channel,HandleMessage);
            MyAPIGateway.Utilities.SendModMessage(Channel,"ApiEndpointRequest");
        }

        internal static void Unload()
        {
            if(registered && MyAPIGateway.Utilities!=null) MyAPIGateway.Utilities.UnregisterMessageHandler(Channel,HandleMessage);
            registered=false; Ready=false;
            shieldInfo=null; modulationInfo=null; shieldBlock=null; shieldStatus=null; facesFast=null; instance=null; facesFailed=false;
        }

        private static void HandleMessage(object message)
        {
            // Other API clients echo an ImmutableDictionary copy back on this channel for Defense Shields' tamper check; skip those.
            if(!(message is IReadOnlyDictionary<string,Delegate> methods) || message.GetType().Name.StartsWith("Immutable")) return;
            if(methods.Count==0) { Ready=false; return; }
            try
            {
                shieldInfo=Get<Func<MyEntity,MyTuple<bool,bool,float,float,float,int>>>(methods,"GetShieldInfo");
                modulationInfo=Get<Func<MyEntity,MyTuple<bool,bool,float,float>>>(methods,"GetModulationInfo");
                shieldBlock=Get<Func<IMyEntity,IMyTerminalBlock>>(methods,"GetShieldBlock");
                shieldStatus=Get<Func<IMyTerminalBlock,string>>(methods,"ShieldStatus");
                facesFast=Get<Func<MyEntity,MyTuple<bool,Vector3I>>>(methods,"GetFacesFast");
                Ready=shieldInfo!=null;
                Logger.Info("Defense Shields API attached; ready="+Ready);
            }
            catch(Exception ex) { Ready=false; Logger.Warning(ex,"Defense Shields API attach failed; VR shield display disabled"); }
        }

        private static T Get<T>(IReadOnlyDictionary<string,Delegate> methods,string name) where T:class =>
            methods.TryGetValue(name,out var method) ? method as T : null;

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

        private static Func<object> instance;
        private static bool facesFailed;
        // Per-face health (0..1, or -1 when this client has no charge base) and online state, read from the mod's synced shield state as its own HUD does. Null when unavailable.
        internal static bool TryFaces(MyEntity grid,out float[] health,out bool[] online)
        {
            health=null; online=null;
            if(!Ready || grid==null) return false;
            try
            {
                if(instance==null)
                {
                    var type=ModReflection.LiveType("DefenseShields.Session","Instance");
                    instance=type==null ? null : ModReflection.StaticReader(type,"Instance");
                }
                var session=instance?.Invoke();
                if(!(ModReflection.Get(session,"IdToBus") is System.Collections.IDictionary bus)) return false;
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
