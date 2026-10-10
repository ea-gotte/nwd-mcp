#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Bimwright.Nwd.Shared.Views.Toast
{
    /// <summary>Session-only bounded sanitized outcomes, independent of toast visibility.</summary>
    public sealed class ActivityHistory
    {
        public const int Capacity = 1000;
        private readonly object _gate = new object();
        private readonly Queue<ToastActivityEntry> _entries = new Queue<ToastActivityEntry>();
        private long _version;
        public long Version { get { lock (_gate) return _version; } }
        public void Record(McpToastViewModel model)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            lock (_gate)
            {
                _entries.Enqueue(new ToastActivityEntry(model.Title, model.Body, model.Success, model.DurationMs));
                while (_entries.Count > Capacity) _entries.Dequeue();
                _version++;
            }
        }
        public ToastActivityEntry[] Snapshot() { lock (_gate) return _entries.Reverse().ToArray(); }
    }
}
