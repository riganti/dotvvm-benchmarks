using System;
using System.Collections.Generic;
using System.Linq;

namespace DotVVM.Benchmarks
{
    public class CallTreeItem
    {
        public CallTreeItem(string name)
        {
            this.Name = name;
        }

        public string Name { get; }
        public ulong Samples { get; set; }
        public ulong IncSamples { get; set; }
        private Dictionary<CallTreeItem, int> _callees = null;
        public IEnumerable<CallTreeItem> Callees => _callees?.Keys ?? Enumerable.Empty<CallTreeItem>();
        public IEnumerable<KeyValuePair<CallTreeItem, int>> CalleesWithSampleCounts => _callees ?? Enumerable.Empty<KeyValuePair<CallTreeItem, int>>();
        public void AddCallee(CallTreeItem cti)
        {
            (_callees ?? (_callees = new Dictionary<CallTreeItem, int>(2))).TryGetValue(cti, out int current);
            _callees[cti] = current + 1;
        }

        public Dictionary<CallTreeItem, int> Callers { get; } = new Dictionary<CallTreeItem, int>(2);
        public void AddCaller(CallTreeItem cti)
        {
            Callers.TryGetValue(cti, out int current);
            Callers[cti] = current + 1;
        }
    }
}