using System;

namespace SpaceEngineersVR.Player.Control
{
    internal sealed class ToolbarGesture
    {
        internal enum Action { None, Unequip, AssignSwitch, OpenWheel, FirstPerson }
        private bool pending,thirdPersonAtPress;
        private DateTime pressedAt;
        private object owner;
        public bool Alternate { get; private set; }
        public int Switch { get; private set; }=-1;

        public void Reset() { pending=false; owner=null; Switch=-1; Alternate=false; }

        public Action Update(bool available,bool pressed,bool held,bool released,object currentOwner,int hoveredSwitch,DateTime now,bool thirdPerson=false,bool alternate=false)
        {
            if(!available || (pending && (!ReferenceEquals(owner,currentOwner) || thirdPersonAtPress!=thirdPerson))) { Reset(); return Action.None; }
            if(pressed) { pending=true; pressedAt=now; owner=currentOwner; Switch=hoveredSwitch; thirdPersonAtPress=thirdPerson; Alternate=alternate; }
            if(!pending) return Action.None;
            if(released)
            {
                pending=false;
                if(thirdPersonAtPress) return Action.FirstPerson;
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
