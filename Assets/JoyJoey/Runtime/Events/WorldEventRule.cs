using System;
using UnityEngine;
using UnityEngine.Events;

namespace JoyJoey.Game.Events
{
    public sealed class WorldEventRule : MonoBehaviour
    {
        enum ConditionMode { All, Any }

        [SerializeField] WorldEventScope _scope;
        [SerializeField] WorldEvent _channel;
        [SerializeField] ConditionMode _conditionMode;
        [SerializeField] WorldEventCondition[] _conditions = Array.Empty<WorldEventCondition>();
        [SerializeField] bool _oncePerInstance;
        [SerializeField] UnityEvent _actions = new UnityEvent();

        WorldEventScope _boundScope;
        WorldEvent _boundChannel;
        bool _ran;

        public UnityEvent Actions => _actions;

        public void Configure(WorldEventScope scope, WorldEvent channel)
        {
            Unbind();
            _scope = scope;
            _channel = channel;
            if (isActiveAndEnabled) Bind();
        }

        void OnEnable() => Bind();
        void OnDisable() => Unbind();

        void Bind()
        {
            if (!_scope || !_channel) return;
            _boundScope = _scope;
            _boundChannel = _channel;
            _boundScope.Subscribe(_boundChannel, Receive);
        }

        void Unbind()
        {
            if (_boundScope) _boundScope.Unsubscribe(_boundChannel, Receive);
            _boundScope = null;
            _boundChannel = null;
        }

        void Receive(WorldEventContext context)
        {
            if (!isActiveAndEnabled || _oncePerInstance && _ran || !ConditionsMatch(context)) return;
            // Consume the rule before running actions that can raise another event.
            _ran = true;
            _actions.Invoke();
        }

        bool ConditionsMatch(WorldEventContext context)
        {
            if (_conditions == null || _conditions.Length == 0) return true;
            bool any = _conditionMode == ConditionMode.Any;
            foreach (var condition in _conditions)
            {
                if (condition && condition.Evaluate(context))
                {
                    if (any) return true;
                }
                else if (!any) return false;
            }
            return !any;
        }
    }
}
