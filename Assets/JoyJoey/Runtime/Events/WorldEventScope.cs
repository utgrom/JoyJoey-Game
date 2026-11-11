using System;
using System.Collections.Generic;
using UnityEngine;

namespace JoyJoey.Game.Events
{
    /// <summary>Dispatches events and owns transient state for one scene or encounter.</summary>
    [DisallowMultipleComponent]
    public sealed class WorldEventScope : MonoBehaviour
    {
        const int DispatchLimit = 256;
        readonly Dictionary<WorldEvent, List<Action<WorldEventContext>>> _listeners = new();
        readonly Queue<WorldEventContext> _pending = new();
        readonly HashSet<string> _flags = new(StringComparer.Ordinal);
        readonly Dictionary<string, int> _counters = new(StringComparer.Ordinal);
        bool _dispatching;
        bool _overflowed;
        int _generation;

        public bool GetFlag(string key) => !string.IsNullOrWhiteSpace(key) && _flags.Contains(key);

        public void SetFlag(string key, bool value)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            if (value) _flags.Add(key);
            else _flags.Remove(key);
        }

        public int GetCounter(string key) => !string.IsNullOrWhiteSpace(key) && _counters.TryGetValue(key, out int value) ? value : 0;

        public int AddToCounter(string key, int amount = 1)
        {
            if (string.IsNullOrWhiteSpace(key)) return 0;
            long sum = (long)GetCounter(key) + amount;
            int value = (int)Math.Min(int.MaxValue, Math.Max(int.MinValue, sum));
            if (value == 0) _counters.Remove(key);
            else _counters[key] = value;
            return value;
        }

        public void Subscribe(WorldEvent channel, Action<WorldEventContext> listener)
        {
            if (!channel || listener == null) return;
            if (!_listeners.TryGetValue(channel, out var listeners))
            {
                listeners = new List<Action<WorldEventContext>>();
                _listeners.Add(channel, listeners);
            }
            if (!listeners.Contains(listener)) listeners.Add(listener);
        }

        public void Unsubscribe(WorldEvent channel, Action<WorldEventContext> listener)
        {
            if (ReferenceEquals(channel, null) || !_listeners.TryGetValue(channel, out var listeners)) return;
            listeners.Remove(listener);
            if (listeners.Count == 0) _listeners.Remove(channel);
        }

        public void Publish(WorldEvent channel, GameObject source = null)
        {
            if (!isActiveAndEnabled || !channel) return;
            if (_pending.Count == DispatchLimit)
            {
                _overflowed = true;
                return;
            }

            _pending.Enqueue(new WorldEventContext(channel, this, source));
            if (_dispatching) return;
            DrainEvents();
        }

        void DrainEvents()
        {
            _dispatching = true;
            int delivered = 0;
            try
            {
                while (isActiveAndEnabled && _pending.Count > 0 && delivered < DispatchLimit)
                {
                    Dispatch(_pending.Dequeue());
                    delivered++;
                }
                if (_overflowed || _pending.Count > 0)
                    Debug.LogWarning($"Joy Joey: se alcanzó el límite de {DispatchLimit} eventos consecutivos. Revisá si hay reglas que se disparan entre sí.", this);
            }
            finally
            {
                _pending.Clear();
                _overflowed = false;
                _dispatching = false;
            }
        }

        void Dispatch(WorldEventContext context)
        {
            if (!context.Channel || !_listeners.TryGetValue(context.Channel, out var listeners)) return;
            int generation = _generation;
            // Rules may subscribe, unsubscribe or raise more events during their actions.
            foreach (var listener in listeners.ToArray())
            {
                if (!isActiveAndEnabled || _generation != generation) break;
                if (!listeners.Contains(listener)) continue;
                try { listener(context); }
                catch (Exception error) { Debug.LogException(error, this); }
            }
        }

        void OnDisable()
        {
            _generation++;
            _pending.Clear();
            _overflowed = false;
        }
    }
}
