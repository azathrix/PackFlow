using Azathrix.PackFlow.Editor.Core;
using UnityEditor;
using UnityEngine;

namespace Azathrix.PackFlow.Editor.UI
{
    /// <summary>
    /// 上传配置编辑窗口
    /// </summary>
    public class UploadConfigEditorWindow : EditorWindow
    {
        private Vector2 _scrollPos;
        private int _editingIndex = -1;

        [MenuItem("Azathrix/PackFlow/上传配置管理", priority = 1002)]
        public static void ShowWindow()
        {
            var window = GetWindow<UploadConfigEditorWindow>("上传配置管理");
            window.minSize = new Vector2(400, 300);
        }

        private void OnGUI()
        {
            var registry = UploadConfigRegistry.instance;

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("添加配置"))
            {
                registry.AddConfig();
                _editingIndex = registry.Configs.Count - 1;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            for (int i = 0; i < registry.Configs.Count; i++)
            {
                var namedConfig = registry.Configs[i];
                EditorGUILayout.BeginVertical("box");

                EditorGUILayout.BeginHorizontal();
                var isSelected = registry.SelectedIndex == i;
                var newSelected = EditorGUILayout.Toggle(isSelected, GUILayout.Width(20));
                if (newSelected && !isSelected)
                    registry.SelectedIndex = i;

                EditorGUILayout.LabelField(namedConfig.name, EditorStyles.boldLabel);

                if (GUILayout.Button(_editingIndex == i ? "收起" : "编辑", GUILayout.Width(45)))
                    _editingIndex = _editingIndex == i ? -1 : i;

                GUI.enabled = registry.Configs.Count > 1;
                if (GUILayout.Button("删除", GUILayout.Width(45)))
                {
                    registry.RemoveConfig(i);
                    if (_editingIndex >= registry.Configs.Count)
                        _editingIndex = -1;
                }
                GUI.enabled = true;
                EditorGUILayout.EndHorizontal();

                if (_editingIndex == i)
                {
                    EditorGUI.indentLevel++;
                    DrawConfigEditor(namedConfig);
                    EditorGUI.indentLevel--;
                }

                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawConfigEditor(UploadConfigRegistry.NamedUploadConfig namedConfig)
        {
            EditorGUI.BeginChangeCheck();

            namedConfig.name = EditorGUILayout.TextField("配置名称", namedConfig.name);
            var config = namedConfig.config;
            config.apiType = (UploadApiType)EditorGUILayout.EnumPopup("API 类型", config.apiType);
            config.endpoint = EditorGUILayout.TextField("服务器地址", config.endpoint);

            if (config.apiType != UploadApiType.LocalHttp)
            {
                config.bucket = EditorGUILayout.TextField("Bucket", config.bucket);
                config.accessKey = EditorGUILayout.TextField("Access Key", config.accessKey);
                config.secretKey = EditorGUILayout.PasswordField("Secret Key", config.secretKey);
            }

            config.projectId = EditorGUILayout.TextField("项目 ID", config.projectId);
            config.version = EditorGUILayout.TextField("版本号", config.version);

            if (EditorGUI.EndChangeCheck())
                UploadConfigRegistry.instance.MarkDirty();
        }
    }
}
