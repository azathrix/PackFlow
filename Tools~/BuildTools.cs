using UnityEditor;
using UnityEngine;
using System.IO;
using Frameworks.Systems.UISystems.Core;

namespace Editor
{
    public static class BuildTools
    {
        [MenuItem("Tools/复制FMOD文件", priority = 102)]
        public static void CopyFmodFiles()
        {
            string projectPath = Application.dataPath.Replace("/Assets", "");
            string parentPath = Directory.GetParent(projectPath)?.FullName;

            string srcDir = Path.Combine(parentPath, "SoundProject", "Build", "Android");
            string dstDir = Path.Combine(Application.dataPath, "FMODBanks", "Android");

            if (!Directory.Exists(srcDir))
            {
                Debug.LogError($"源目录不存在: {srcDir}");
                return;
            }

            if (!Directory.Exists(dstDir))
                Directory.CreateDirectory(dstDir);

            foreach (var file in Directory.GetFiles(srcDir))
            {
                string fileName = Path.GetFileName(file) + ".bytes";
                string dstPath = Path.Combine(dstDir, fileName);
                File.Copy(file, dstPath, true);
                Debug.Log($"复制: {fileName}");
            }

            AssetDatabase.Refresh();
            Debug.Log("FMOD文件复制完成");
        }

        [MenuItem("GameObject/复制路径(不含Root)", false, 0)]
        public static void CopyPath()
        {
            var go = Selection.activeGameObject;
            if (go == null) return;

            var path = go.name;
            var t = go.transform.parent;
            var root = t.parent.GetComponentInParent<Panel>();
            while (t != null && t.parent != null && t != root.transform)
            {
                path = t.name + "/" + path;
                t = t.parent;
            }

            GUIUtility.systemCopyBuffer = path;
            Debug.Log($"已复制路径: {path}");
        }
    }
}
