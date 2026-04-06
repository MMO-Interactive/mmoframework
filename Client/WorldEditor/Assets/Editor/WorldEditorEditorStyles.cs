using UnityEditor;
using UnityEngine;

namespace RiseOfHeroes.WorldEditor
{
internal static class WorldEditorEditorStyles
{
    private static GUIStyle _panelStyle;
    private static GUIStyle _sectionTitleStyle;
    private static GUIStyle _mutedLabelStyle;
    private static GUIStyle _heroStyle;

    public static GUIStyle PanelStyle
    {
        get
        {
            if (_panelStyle == null)
            {
                _panelStyle = new GUIStyle("HelpBox")
                {
                    padding = new RectOffset(12, 12, 10, 12),
                    margin = new RectOffset(0, 0, 0, 8)
                };
            }

            return _panelStyle;
        }
    }

    public static GUIStyle SectionTitleStyle
    {
        get
        {
            if (_sectionTitleStyle == null)
            {
                _sectionTitleStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 12
                };
            }

            return _sectionTitleStyle;
        }
    }

    public static GUIStyle MutedLabelStyle
    {
        get
        {
            if (_mutedLabelStyle == null)
            {
                _mutedLabelStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    wordWrap = true
                };
            }

            return _mutedLabelStyle;
        }
    }

    public static GUIStyle HeroStyle
    {
        get
        {
            if (_heroStyle == null)
            {
                _heroStyle = new GUIStyle(EditorStyles.largeLabel)
                {
                    fontStyle = FontStyle.Bold,
                    fontSize = 16
                };
            }

            return _heroStyle;
        }
    }

    public static PanelScope Panel(string title, string subtitle = null)
    {
        return new PanelScope(title, subtitle);
    }

    public readonly struct PanelScope : System.IDisposable
    {
        public PanelScope(string title, string subtitle)
        {
            EditorGUILayout.BeginVertical(PanelStyle);
            EditorGUILayout.LabelField(title, SectionTitleStyle);
            if (!string.IsNullOrWhiteSpace(subtitle))
            {
                EditorGUILayout.LabelField(subtitle, MutedLabelStyle);
            }
            EditorGUILayout.Space(4f);
        }

        public void Dispose()
        {
            EditorGUILayout.EndVertical();
        }
    }
}
}
