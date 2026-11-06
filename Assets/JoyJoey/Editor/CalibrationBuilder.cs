using System;
using System.IO;
using System.Linq;
using JoyJoey.Game.Display;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace JoyJoey.Game.Editor
{
    /// <summary>Adds one calibration feature without replacing the renderer's authored features.</summary>
    public static class CalibrationBuilder
    {
        public const string ShaderPath = "Assets/JoyJoey/Shaders/DisplayCalibration.shader";
        public const string MaterialPath = "Assets/JoyJoey/Display/DisplayCalibration.mat";
        public static readonly string[] RendererPaths = { "Assets/Settings/PC_Renderer.asset", "Assets/Settings/Mobile_Renderer.asset" };

        public static void EnsureAssets()
        {
            EnsureFolder("Assets/JoyJoey/Display");
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (!material)
            {
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
                if (!shader) throw new InvalidOperationException("No se encontró el shader de calibración: " + ShaderPath);
                material = new Material(shader) { name = "DisplayCalibration" };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            foreach (string path in RendererPaths)
            {
                var renderer = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(path);
                if (!renderer) throw new InvalidOperationException("No se encontró el renderer del proyecto: " + path);
                bool changed = false;
                var feature = renderer.rendererFeatures.OfType<CalibrationRendererFeature>().FirstOrDefault();
                if (!feature)
                {
                    feature = ScriptableObject.CreateInstance<CalibrationRendererFeature>();
                    feature.name = "Joy Joey · Calibración de imagen";
                    feature.Material = material;
                    AssetDatabase.AddObjectToAsset(feature, renderer);
                    renderer.rendererFeatures.Add(feature);
                    changed = true;
                }
                else if (!feature.Material) { feature.Material = material; changed = true; }
                if (!changed) continue;
                renderer.SetDirty();
                EditorUtility.SetDirty(feature);
                EditorUtility.SetDirty(renderer);
                // Keep Unity's local-ID map aligned so features survive domain reload/import.
                var serialized = new SerializedObject(renderer);
                var map = serialized.FindProperty("m_RendererFeatureMap");
                map.arraySize = renderer.rendererFeatures.Count;
                for (int i = 0; i < renderer.rendererFeatures.Count; i++)
                    if (renderer.rendererFeatures[i] && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(renderer.rendererFeatures[i], out string _, out long id))
                        map.GetArrayElementAtIndex(i).longValue = id;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.SaveAssetIfDirty(renderer);
            }
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
