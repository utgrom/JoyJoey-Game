using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

namespace JoyJoey.Game.Editor
{
    /// <summary>Editor-only bridge to Unity's mixer authoring API. Runtime uses public mixer APIs.</summary>
    internal static class AudioMixerAuthoring
    {
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        public static AudioMixer Create(string path)
        {
            Type controllerType = FindType("UnityEditor.Audio.AudioMixerController");
            var mixer = (AudioMixer)controllerType.GetMethod("CreateMixerControllerAtPath", Flags).Invoke(null, new object[] { path });
            var master = Get(mixer, "masterGroup");
            var normal = (AudioMixerSnapshot)Get(mixer, "TargetSnapshot") ?? (AudioMixerSnapshot)Get(mixer, "startSnapshot");
            normal.name = "Normal";
            var music = Group(mixer, master, "Music");
            var sfx = Group(mixer, master, "SFX");
            var ui = Group(mixer, master, "UI");
            var musicEffects = Group(mixer, music, "MusicEffects");
            var sfxEffects = Group(mixer, sfx, "SfxEffects");
            var uiEffects = Group(mixer, ui, "UiEffects");
            Expose(mixer, new[] { master, music, sfx, ui }, new[] { "MasterVolume", "MusicVolume", "SfxVolume", "UiVolume" });

            object lowpass = Effect(mixer, sfxEffects, "Lowpass", false);
            Effect(mixer, sfxEffects, "Highpass", true);
            Effect(mixer, sfxEffects, "Echo", true);
            SetEffect(lowpass, mixer, normal, "Cutoff freq", 22000);
            SetEffect(lowpass, mixer, normal, "Resonance", 1);
            SetVolume(musicEffects, mixer, normal, 0);
            SetVolume(sfxEffects, mixer, normal, 0);
            SetVolume(uiEffects, mixer, normal, 0);

            var pause = CloneSnapshot(mixer, normal, "Pause");
            SetVolume(musicEffects, mixer, pause, -8);
            SetVolume(sfxEffects, mixer, pause, -6);
            SetVolume(uiEffects, mixer, pause, 0);
            SetEffect(lowpass, mixer, pause, "Cutoff freq", 2200);

            var cutscene = CloneSnapshot(mixer, normal, "Cutscene");
            SetVolume(musicEffects, mixer, cutscene, -3);
            SetVolume(sfxEffects, mixer, cutscene, -3);
            SetVolume(uiEffects, mixer, cutscene, 0);
            Set(mixer, "TargetSnapshot", normal);
            Set(mixer, "startSnapshot", normal);
            SetView(mixer, new[] { master, music, musicEffects, sfx, sfxEffects, ui, uiEffects });
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path)) EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(mixer);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            return AssetDatabase.LoadAssetAtPath<AudioMixer>(path);
        }

        static object Group(object mixer, object parent, string name)
        {
            object group = Call(mixer, "CreateNewGroup", name, false);
            Call(mixer, "AddChildToParent", group, parent);
            return group;
        }
        static void SetVolume(object group, object mixer, object snapshot, float value)
            => Call(group, "SetValueForVolume", mixer, snapshot, value);

        static AudioMixerSnapshot CloneSnapshot(object mixer, AudioMixerSnapshot source, string name)
        {
            Set(mixer, "TargetSnapshot", source);
            Call(mixer, "CloneNewSnapshotFromTarget", false);
            var snapshot = (AudioMixerSnapshot)Get(mixer, "TargetSnapshot");
            snapshot.name = name;
            return snapshot;
        }

        static object Effect(AudioMixer mixer, object group, string name, bool bypass)
        {
            Type type = FindType("UnityEditor.Audio.AudioMixerEffectController");
            object effect = Activator.CreateInstance(type, Flags, null, new object[] { name }, null);
            Call(effect, "PreallocateGUIDs");
            Set(effect, "bypass", bypass);
            AssetDatabase.AddObjectToAsset((UnityEngine.Object)effect, mixer);
            var existing = (Array)Get(group, "effects");
            Array effects = Array.CreateInstance(type, existing.Length + 1);
            Array.Copy(existing, effects, existing.Length);
            effects.SetValue(effect, existing.Length);
            Set(group, "effects", effects);
            return effect;
        }
        static void SetEffect(object effect, object mixer, object snapshot, string parameter, float value)
            => Call(effect, "SetValueForParameter", mixer, snapshot, parameter, value);

        static void Expose(object mixer, object[] groups, string[] names)
        {
            var property = mixer.GetType().GetProperty("exposedParameters", Flags);
            Type element = property.PropertyType.GetElementType();
            Array exposed = Array.CreateInstance(element, groups.Length);
            for (int i = 0; i < groups.Length; i++)
            {
                object parameter = Activator.CreateInstance(element);
                element.GetField("name", Flags).SetValue(parameter, names[i]);
                element.GetField("guid", Flags).SetValue(parameter, Call(groups[i], "GetGUIDForVolume"));
                exposed.SetValue(parameter, i);
            }
            property.SetValue(mixer, exposed);
        }

        static void SetView(object mixer, object[] groups)
        {
            var property = mixer.GetType().GetProperty("views", Flags);
            Type viewType = property.PropertyType.GetElementType();
            object view = Activator.CreateInstance(viewType);
            var guidField = viewType.GetField("guids", Flags);
            Array guids = Array.CreateInstance(guidField.FieldType.GetElementType(), groups.Length);
            for (int i = 0; i < groups.Length; i++) guids.SetValue(Get(groups[i], "groupID"), i);
            guidField.SetValue(view, guids);
            viewType.GetField("name", Flags).SetValue(view, "Todos los canales");
            Array views = Array.CreateInstance(viewType, 1);
            views.SetValue(view, 0);
            property.SetValue(mixer, views);
            Set(mixer, "currentViewIndex", 0);
        }

        static Type FindType(string name)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(name, false);
                if (type != null) return type;
            }
            throw new InvalidOperationException("Esta versión de Unity no ofrece la herramienta de creación del mezclador: " + name);
        }
        static object Get(object target, string property)
            => target.GetType().GetProperty(property, Flags).GetValue(target);
        static void Set(object target, string property, object value)
            => target.GetType().GetProperty(property, Flags).SetValue(target, value);
        static object Call(object target, string name, params object[] args)
        {
            var method = target.GetType().GetMethods(Flags).FirstOrDefault(m => m.Name == name &&
                m.GetParameters().Length == args.Length && m.GetParameters().Select((p, i) => args[i] == null || p.ParameterType.IsInstanceOfType(args[i])).All(v => v));
            if (method == null) throw new MissingMethodException(target.GetType().FullName, name);
            return method.Invoke(target, args);
        }
    }
}
