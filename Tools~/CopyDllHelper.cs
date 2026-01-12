using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Editor
{
    public static class CopyDllHelper
    {
        [MenuItem("Tools/拷贝Dll到工程")]
        public static void CopyDllToProject()
        {
            var files = new List<string>(RuntimeConfigInitializer.GetConfig().hotUpdateDlls);
            Copy(files.ToArray(), $"HybridCLRData\\HotUpdateDlls\\{EditorUserBuildSettings.activeBuildTarget}");

            var aotList = GetAOTAssemblyList();
            if (aotList != null)
            {
                Copy(aotList.ToArray(),
                    $"HybridCLRData\\AssembliesPostIl2CppStrip\\{EditorUserBuildSettings.activeBuildTarget}");
            }

            AssetDatabase.Refresh();
        }

        static List<string> GetAOTAssemblyList()
        {
            var aotType = Type.GetType("AOTGenericReferences");
            if (aotType == null)
            {
                Debug.LogWarning("AOTGenericReferences type not found");
                return null;
            }
            var listField = aotType.GetField("PatchedAOTAssemblyList", BindingFlags.Public | BindingFlags.Static);
            if (listField == null)
            {
                Debug.LogWarning("PatchedAOTAssemblyList field not found");
                return null;
            }
            return listField.GetValue(null) as List<string>;
        } 
 
        static void Copy(string[] files, string folder)
        {
            var p = Application.dataPath.Replace("/Assets", "");
            var m = Path.Combine(p, folder);
            var targetPath = Path.Combine(Application.dataPath, "AssemblyHotUpdate");

            // 容错：源目录不存在则跳过
            if (!Directory.Exists(m))
            {
                Debug.LogWarning($"[CopyDll] 源目录不存在，跳过: {m}");
                return;
            }

            // 确保目标目录存在
            if (!Directory.Exists(targetPath))
                Directory.CreateDirectory(targetPath);

            foreach (var file in files)
            {
                try
                {
                    var sourcePath = Path.Combine(m, file);
                    // 容错：源文件不存在则跳过
                    if (!File.Exists(sourcePath))
                    {
                        Debug.LogWarning($"[CopyDll] 源文件不存在，跳过: {file}");
                        continue;
                    }

                    var t = Path.Combine(targetPath, file + ".bytes");
                    if (File.Exists(t))
                        File.Delete(t);

                    File.Copy(sourcePath, t);
                    Debug.Log("拷贝Dll:" + file);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }
    }
}