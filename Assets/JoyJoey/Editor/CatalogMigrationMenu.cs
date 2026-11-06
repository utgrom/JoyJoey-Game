using System.IO;
using UnityEditor;
using UnityEngine;

namespace JoyJoey.Sandbox.Editor
{
    public static class CatalogMigrationMenu
    {
        [MenuItem("Joy Joey/Actualizar catálogo al taller de ataques")]
        public static void UpgradeSelected()
        {
            var catalog=Selection.activeObject as CombatCatalog;
            if(catalog==null)catalog=AssetDatabase.LoadAssetAtPath<CombatCatalog>("Assets/JoyJoey/Data/SandboxCombat.asset");
            if(catalog!=null)UpgradeWithBackup(catalog);
        }
        public static bool UpgradeWithBackup(CombatCatalog catalog)
        {
            if(catalog==null || catalog.SchemaVersion>=CatalogMigration.CurrentVersion)return false;
            string assetPath=AssetDatabase.GetAssetPath(catalog);
            if(!string.IsNullOrEmpty(assetPath))
            {
                string parent=Path.GetDirectoryName(assetPath).Replace(Path.DirectorySeparatorChar,'/');
                string backupFolder=parent+"/Backups";
                if(!AssetDatabase.IsValidFolder(backupFolder))AssetDatabase.CreateFolder(parent,"Backups");
                string backupPath=AssetDatabase.GenerateUniqueAssetPath(backupFolder+"/"+Path.GetFileNameWithoutExtension(assetPath)+"_BeforeAuthoring.asset");
                if(!AssetDatabase.CopyAsset(assetPath,backupPath))throw new IOException("No se pudo crear el respaldo del catálogo: "+backupPath);
            }
            Undo.RecordObject(catalog,"Actualizar catálogo de ataques");
            bool changed=CatalogMigration.Upgrade(catalog);
            if(changed){EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();}
            return changed;
        }
    }
}
