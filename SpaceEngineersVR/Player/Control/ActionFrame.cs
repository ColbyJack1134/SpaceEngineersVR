using System.Collections.Generic;
using VRage.Input;
using VRage.Utils;

namespace SpaceEngineersVR.Player.Control
{
    internal sealed class ActionFrame
    {
        private HashSet<MyStringId> current = new HashSet<MyStringId>();
        private HashSet<MyStringId> previous = new HashSet<MyStringId>();
        private readonly HashSet<MyStringId> queued = new HashSet<MyStringId>();
        public void Queue(MyStringId action) => queued.Add(action);
        public void Advance(bool enabled)
        {
            var reuse = previous; previous = current; current = reuse;
            current.Clear();
            if (enabled) current.UnionWith(queued);
            queued.Clear();
        }
        public bool Read(MyStringId action, MyControlStateType type)
        {
            switch (type)
            {
                case MyControlStateType.PRESSED: return current.Contains(action);
                case MyControlStateType.NEW_PRESSED:
                case MyControlStateType.NEW_PRESSED_REPEATING: return current.Contains(action) && !previous.Contains(action);
                case MyControlStateType.NEW_RELEASED: return previous.Contains(action) && !current.Contains(action);
                default: return false;
            }
        }
        public void Reset() { current.Clear(); previous.Clear(); queued.Clear(); }
    }
}
