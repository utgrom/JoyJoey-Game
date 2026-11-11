using System.Collections.Generic;
using System.Text.RegularExpressions;
using JoyJoey.Game.Events;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace JoyJoey.Tests
{
    public sealed class WorldEventTests
    {
        GameObject _world;
        WorldEventScope _scope;
        WorldEvent _entered;
        WorldEvent _followUp;

        [SetUp]
        public void CreateWorld()
        {
            _world = new GameObject("World event test");
            _scope = _world.AddComponent<WorldEventScope>();
            _entered = ScriptableObject.CreateInstance<WorldEvent>();
            _followUp = ScriptableObject.CreateInstance<WorldEvent>();
        }

        [TearDown]
        public void DestroyWorld()
        {
            Object.DestroyImmediate(_world);
            Object.DestroyImmediate(_entered);
            Object.DestroyImmediate(_followUp);
        }

        [Test]
        public void NestedEventsWaitForCurrentListenersAndKeepTheirSource()
        {
            var order = new List<string>();
            WorldEventContext nested = default;
            _scope.Subscribe(_entered, _ =>
            {
                order.Add("first listener");
                _scope.Publish(_followUp, _world);
            });
            _scope.Subscribe(_entered, _ => order.Add("second listener"));
            _scope.Subscribe(_followUp, context =>
            {
                order.Add("follow-up");
                nested = context;
            });

            _scope.Publish(_entered);

            Assert.That(order, Is.EqualTo(new[] { "first listener", "second listener", "follow-up" }));
            Assert.That(nested.Channel, Is.SameAs(_followUp));
            Assert.That(nested.Scope, Is.SameAs(_scope));
            Assert.That(nested.Source, Is.SameAs(_world));
        }

        [Test]
        public void OneTimeRuleWaitsForItsConditionAndIgnoresItsOwnFollowUp()
        {
            var condition = _world.AddComponent<WorldFlagCondition>();
            var conditionSettings = new SerializedObject(condition);
            conditionSettings.FindProperty("_key").stringValue = "ready";
            conditionSettings.ApplyModifiedPropertiesWithoutUndo();
            var rule = _world.AddComponent<WorldEventRule>();
            var ruleSettings = new SerializedObject(rule);
            var conditions = ruleSettings.FindProperty("_conditions");
            conditions.arraySize = 1;
            conditions.GetArrayElementAtIndex(0).objectReferenceValue = condition;
            ruleSettings.FindProperty("_oncePerInstance").boolValue = true;
            ruleSettings.ApplyModifiedPropertiesWithoutUndo();
            rule.Configure(_scope, _entered);
            int calls = 0;
            rule.Actions.AddListener(() =>
            {
                calls++;
                _scope.Publish(_entered);
            });

            _scope.Publish(_entered);
            Assert.That(calls, Is.Zero);
            _scope.SetFlag("ready", true);
            _scope.Publish(_entered);
            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public void AnEventCycleStopsWithoutBlockingLaterPublishes()
        {
            int cycleCalls = 0, followUpCalls = 0;
            _scope.Subscribe(_entered, _ =>
            {
                cycleCalls++;
                _scope.Publish(_entered);
            });
            _scope.Subscribe(_followUp, _ => followUpCalls++);
            LogAssert.Expect(LogType.Warning, new Regex("límite de 256 eventos consecutivos"));

            _scope.Publish(_entered);
            _scope.Publish(_followUp);

            Assert.That(cycleCalls, Is.EqualTo(256));
            Assert.That(followUpCalls, Is.EqualTo(1));
        }
    }
}
