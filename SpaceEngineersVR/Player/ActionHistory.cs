using System;
using System.Collections.Generic;
using System.Linq;

namespace SpaceEngineersVR.Player
{
    internal sealed class ActionHistory
    {
        private const int Limit=20;
        private readonly List<string> keys;
        internal string[] Keys => keys.ToArray();
        internal ActionHistory(IEnumerable<string> saved=null)
        {
            keys=(saved ?? Array.Empty<string>()).Where(key=>!string.IsNullOrWhiteSpace(key))
                .Distinct(StringComparer.Ordinal).Take(Limit).ToList();
        }
        internal void Record(string key)
        {
            if(string.IsNullOrWhiteSpace(key)) return;
            keys.Remove(key); keys.Insert(0,key);
            if(keys.Count>Limit) keys.RemoveAt(Limit);
        }
        internal ActionChoice[] Recent(ActionChoice[] choices,int limit) => Order(choices)
            .Where(action=>keys.Contains(action.HistoryKey)).Take(limit).ToArray();
        internal ActionChoice[] Order(ActionChoice[] choices)
        {
            var rank=keys.Select((key,index)=>new {key,index}).ToDictionary(item=>item.key,item=>item.index,StringComparer.Ordinal);
            return choices.OrderBy(action=>action.HistoryKey!=null && rank.TryGetValue(action.HistoryKey,out var index) ? index:int.MaxValue).ToArray();
        }
    }
}
