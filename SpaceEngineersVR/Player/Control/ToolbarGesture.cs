using System;

namespace SpaceEngineersVR.Player.Control
{
    internal sealed class ToolbarGesture
    {
        internal enum Action { None, Unequip, AssignSwitch, OpenWheel }
        private bool pending;
        private DateTime pressedAt;
        private object owner;
        public int Switch { get; private set; }=-1;

        public void Reset() { pending=false; owner=null; Switch=-1; }

        public Action Update(bool available,bool pressed,bool held,bool released,object currentOwner,int hoveredSwitch,DateTime now)
        {
            if(!available || (pending && !ReferenceEquals(owner,currentOwner))) { Reset(); return Action.None; }
            if(pressed) { pending=true; pressedAt=now; owner=currentOwner; Switch=hoveredSwitch; }
            if(!pending) return Action.None;
            if(released)
            {
                pending=false;
                // Moving to a different switch cancels assignment, rather than editing the wrong slot or unequipping.
                return Switch<0 ? Action.Unequip : hoveredSwitch==Switch ? Action.AssignSwitch : Action.None;
            }
            if(!held) { Reset(); return Action.None; }
            if((now-pressedAt).TotalSeconds<.28) return Action.None;
            Reset();
            return Action.OpenWheel;
        }
    }
}
