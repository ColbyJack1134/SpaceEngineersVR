using System;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using System.Linq;
using VRageMath;
using SpaceEngineersVR.Plugin;

namespace SpaceEngineersVR.Player
{
    internal static class WristTargeting
    {
        internal enum Request { None,Lock,Cancel,Unlock }
        internal sealed class State
        {
            public long Owner,Target;
            public Request Kind;
            public string Label => Kind==Request.Lock ? "Lock target":Kind==Request.Cancel ? "Cancel lock":Kind==Request.Unlock ? "Unlock target":"No target";
        }
        private static DateTime nextRequest;
        internal static State Current
        {
            get
            {
                var session=MySession.Static;
                var character=session?.LocalCharacter;
                var owner=session?.ControlledEntity;
                if(!Main.WorldAvailable || !InputRouter.Gameplay || Main.MenuOpen || character==null || character.IsDead ||
                    !(owner is IMyTargetingCapableBlock capable) || !capable.IsTargetLockingEnabled() || capable.IsShipToolSelected() ||
                    !(owner is MyCubeBlock block) || !block.IsWorking || !block.CubeGrid.IsPowerSwitchOn) return new State();
                var locking=character.Components.Get<MyTargetLockingComponent>();
                var focus=character.Components.Get<MyTargetFocusComponent>();
                if(locking==null || focus==null || !focus.IsLocallyControlled) return new State();
                var locked=locking.TargetEntity;
                var target=locked ?? focus.CurrentTarget;
                if(target==null || target.Closed || target.MarkedForClose) return new State();
                return new State {Owner=block.EntityId,Target=target.EntityId,
                    Kind=locked==null ? Request.Lock:locking.IsTargetLocked ? Request.Unlock:Request.Cancel};
            }
        }
        internal static bool Matches(State expected,State current) => expected!=null && current!=null && expected.Kind!=Request.None &&
            expected.Owner==current.Owner && expected.Target==current.Target && expected.Kind==current.Kind;
        internal static SurfaceKey Key(State state,Vector2 uv,float radius=.0675f) => new SurfaceKey(state.Label,uv.X-radius,uv.Y-radius*16/9,2*radius,2*radius*16/9) {
            TargetId=state.Target,Enabled=state.Kind!=Request.None,Invisible=true,DirectOnly=true,Round=true,
            Action=new ActionChoice(state.Label,()=>Activate(state)) };
        internal static void AddKey(SurfaceView panel,WorldMarkers.View snapshot,MatrixD head,SignalLayout.Options options,DateTime now)
        {
            var state=Current;
            if(state.Kind==Request.None || snapshot==null || (now-snapshot.Time).TotalSeconds>1) return;
            var candidate=WristSignals.Resolve(snapshot,panel,head.Translation,options,now).Candidates.FirstOrDefault(c=>c.Marker.Ring!=null && c.Marker.Id=="lock:"+state.Target);
            if(candidate==null || WristKnob.Reserved.Contains(candidate.UV)) return;
            panel.Keys=panel.Keys.Concat(new[] {Key(state,candidate.UV)}).ToArray();
        }
        private static void Activate(State expected)
        {
            if(DateTime.UtcNow<nextRequest) return;
            if(!Matches(expected,Current)) { EssentialHud.Notify("Target changed"); return; }
            var character=MySession.Static.LocalCharacter;
            nextRequest=DateTime.UtcNow.AddMilliseconds(300);
            // Use native requests directly; a synthetic secondary-tool press can also fire a weapon.
            if(expected.Kind==Request.Lock) character.Components.Get<MyTargetFocusComponent>().OnLockRequest();
            else character.Components.Get<MyTargetLockingComponent>().ReleaseTargetLockRequest();
        }
    }
}
