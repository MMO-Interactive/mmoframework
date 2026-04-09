using System;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MMONetworking.UnityEditorCodex;

public static class CodexUnityEditorBridge
{
    public static CodexCommandResult OpenScene(string sceneAssetPath)
    {
        if (string.IsNullOrWhiteSpace(sceneAssetPath))
        {
            return CodexCommandResult.Fail("Scene path is required.");
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return CodexCommandResult.Fail("Scene switch canceled by user.");
        }

        var scene = EditorSceneManager.OpenScene(sceneAssetPath, OpenSceneMode.Single);
        return CodexCommandResult.Success("Opened scene: " + scene.path);
    }

    public static CodexCommandResult OpenSceneAdditive(string sceneAssetPath)
    {
        if (string.IsNullOrWhiteSpace(sceneAssetPath))
        {
            return CodexCommandResult.Fail("Scene path is required.");
        }

        var scene = EditorSceneManager.OpenScene(sceneAssetPath, OpenSceneMode.Additive);
        return CodexCommandResult.Success("Opened scene additively: " + scene.path);
    }

    public static CodexCommandResult CreateSceneAsset(string sceneName, string sceneFolder = "Assets/Scenes")
    {
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            return CodexCommandResult.Fail("sceneName is required.");
        }

        if (string.IsNullOrWhiteSpace(sceneFolder))
        {
            sceneFolder = "Assets/Scenes";
        }

        if (!AssetDatabase.IsValidFolder(sceneFolder))
        {
            return CodexCommandResult.Fail("Scene folder does not exist: " + sceneFolder);
        }

        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        var sceneAssetPath = sceneFolder.TrimEnd('/') + "/" + sceneName + ".unity";
        if (!EditorSceneManager.SaveScene(scene, sceneAssetPath))
        {
            return CodexCommandResult.Fail("Failed to create scene at path: " + sceneAssetPath);
        }

        AssetDatabase.Refresh();
        return CodexCommandResult.Success("Created scene: " + sceneAssetPath);
    }

    public static CodexCommandResult CloseScene(string sceneAssetPath, bool removeSceneObjects = true)
    {
        if (string.IsNullOrWhiteSpace(sceneAssetPath))
        {
            return CodexCommandResult.Fail("scene path is required.");
        }

        for (var i = 0; i < EditorSceneManager.sceneCount; i++)
        {
            var scene = EditorSceneManager.GetSceneAt(i);
            if (!string.Equals(scene.path, sceneAssetPath, StringComparison.Ordinal))
            {
                continue;
            }

            if (!EditorSceneManager.CloseScene(scene, removeSceneObjects))
            {
                return CodexCommandResult.Fail("Failed to close scene: " + sceneAssetPath);
            }

            return CodexCommandResult.Success("Closed scene: " + sceneAssetPath);
        }

        return CodexCommandResult.Fail("Scene is not currently open: " + sceneAssetPath);
    }

    public static CodexCommandResult CreateGameObject(string objectName, string parentHierarchyPath = "")
    {
        if (string.IsNullOrWhiteSpace(objectName))
        {
            objectName = "New Codex Object";
        }

        var gameObject = new GameObject(objectName);
        var parentTransform = FindTransform(parentHierarchyPath);
        if (parentTransform != null)
        {
            gameObject.transform.SetParent(parentTransform, false);
        }

        Undo.RegisterCreatedObjectUndo(gameObject, "Codex Create GameObject");
        Selection.activeGameObject = gameObject;
        EditorGUIUtility.PingObject(gameObject);
        return CodexCommandResult.Success("Created GameObject: " + GetHierarchyPath(gameObject.transform));
    }

    public static CodexCommandResult CreatePrimitive(string primitiveTypeName, string objectName, string parentHierarchyPath = "")
    {
        PrimitiveType primitiveType;
        if (!TryParsePrimitiveType(primitiveTypeName, out primitiveType))
        {
            return CodexCommandResult.Fail("Unsupported primitive type: " + primitiveTypeName);
        }

        var primitive = GameObject.CreatePrimitive(primitiveType);
        primitive.name = string.IsNullOrWhiteSpace(objectName) ? primitiveType.ToString() : objectName;

        var parentTransform = FindTransform(parentHierarchyPath);
        if (parentTransform != null)
        {
            primitive.transform.SetParent(parentTransform, false);
        }

        Undo.RegisterCreatedObjectUndo(primitive, "Codex Create Primitive");
        Selection.activeGameObject = primitive;
        return CodexCommandResult.Success("Created primitive: " + GetHierarchyPath(primitive.transform));
    }

    public static CodexCommandResult DeleteGameObject(string hierarchyPath)
    {
        var target = FindTransform(hierarchyPath);
        if (target == null)
        {
            return CodexCommandResult.Fail("Could not find GameObject at path: " + hierarchyPath);
        }

        Undo.DestroyObjectImmediate(target.gameObject);
        return CodexCommandResult.Success("Deleted GameObject: " + hierarchyPath);
    }

    public static CodexCommandResult DuplicateGameObject(string hierarchyPath)
    {
        var target = FindTransform(hierarchyPath);
        if (target == null)
        {
            return CodexCommandResult.Fail("Could not find GameObject at path: " + hierarchyPath);
        }

        var duplicate = UnityEngine.Object.Instantiate(target.gameObject, target.parent);
        duplicate.name = target.gameObject.name + " Copy";
        Undo.RegisterCreatedObjectUndo(duplicate, "Codex Duplicate GameObject");
        Selection.activeGameObject = duplicate;
        return CodexCommandResult.Success("Duplicated GameObject: " + GetHierarchyPath(duplicate.transform));
    }

    public static CodexCommandResult RenameGameObject(string hierarchyPath, string newName)
    {
        var target = FindTransform(hierarchyPath);
        if (target == null)
        {
            return CodexCommandResult.Fail("Could not find GameObject at path: " + hierarchyPath);
        }

        if (string.IsNullOrWhiteSpace(newName))
        {
            return CodexCommandResult.Fail("New name is required.");
        }

        Undo.RecordObject(target.gameObject, "Codex Rename GameObject");
        target.name = newName;
        EditorUtility.SetDirty(target.gameObject);
        return CodexCommandResult.Success("Renamed object to: " + GetHierarchyPath(target));
    }

    public static CodexCommandResult SetActive(string hierarchyPath, bool isActive)
    {
        var target = FindTransform(hierarchyPath);
        if (target == null)
        {
            return CodexCommandResult.Fail("Could not find GameObject at path: " + hierarchyPath);
        }

        Undo.RecordObject(target.gameObject, "Codex Set Active");
        target.gameObject.SetActive(isActive);
        EditorUtility.SetDirty(target.gameObject);
        return CodexCommandResult.Success("Set active=" + isActive + " for: " + hierarchyPath);
    }

    public static CodexCommandResult InstantiatePrefab(string prefabAssetPath, string parentHierarchyPath = "")
    {
        if (string.IsNullOrWhiteSpace(prefabAssetPath))
        {
            return CodexCommandResult.Fail("Prefab asset path is required.");
        }

        var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabAssetPath);
        if (prefabAsset == null)
        {
            return CodexCommandResult.Fail("Could not load prefab asset at path: " + prefabAssetPath);
        }

        var instance = PrefabUtility.InstantiatePrefab(prefabAsset) as GameObject;
        if (instance == null)
        {
            return CodexCommandResult.Fail("Failed to instantiate prefab: " + prefabAssetPath);
        }

        var parentTransform = FindTransform(parentHierarchyPath);
        if (parentTransform != null)
        {
            instance.transform.SetParent(parentTransform, false);
        }

        Undo.RegisterCreatedObjectUndo(instance, "Codex Instantiate Prefab");
        Selection.activeGameObject = instance;
        return CodexCommandResult.Success("Instantiated prefab: " + GetHierarchyPath(instance.transform));
    }

    public static CodexCommandResult SetTransform(string hierarchyPath, Vector3 position, Vector3 eulerRotation, Vector3 localScale)
    {
        var target = FindTransform(hierarchyPath);
        if (target == null)
        {
            return CodexCommandResult.Fail("Could not find GameObject at path: " + hierarchyPath);
        }

        Undo.RecordObject(target, "Codex Set Transform");
        target.localPosition = position;
        target.localEulerAngles = eulerRotation;
        target.localScale = localScale;
        EditorUtility.SetDirty(target);

        return CodexCommandResult.Success("Updated transform for: " + hierarchyPath);
    }

    public static CodexCommandResult ResetTransform(string hierarchyPath)
    {
        var target = FindTransform(hierarchyPath);
        if (target == null)
        {
            return CodexCommandResult.Fail("Could not find GameObject at path: " + hierarchyPath);
        }

        Undo.RecordObject(target, "Codex Reset Transform");
        target.localPosition = Vector3.zero;
        target.localEulerAngles = Vector3.zero;
        target.localScale = Vector3.one;
        EditorUtility.SetDirty(target);
        return CodexCommandResult.Success("Reset local transform for: " + hierarchyPath);
    }

    public static CodexCommandResult AddComponent(string hierarchyPath, string componentTypeName)
    {
        var target = FindTransform(hierarchyPath);
        if (target == null)
        {
            return CodexCommandResult.Fail("Could not find GameObject at path: " + hierarchyPath);
        }

        if (string.IsNullOrWhiteSpace(componentTypeName))
        {
            return CodexCommandResult.Fail("Component type name is required.");
        }

        var componentType = ResolveType(componentTypeName);
        if (componentType == null)
        {
            return CodexCommandResult.Fail("Could not resolve component type: " + componentTypeName);
        }

        if (!typeof(Component).IsAssignableFrom(componentType))
        {
            return CodexCommandResult.Fail(componentTypeName + " is not a Unity Component type.");
        }

        Undo.AddComponent(target.gameObject, componentType);
        return CodexCommandResult.Success("Added component " + componentType.FullName + " to " + hierarchyPath);
    }

    public static CodexCommandResult SetComponentProperty(string hierarchyPath, string componentTypeName, string propertyName, string propertyValue)
    {
        var target = FindTransform(hierarchyPath);
        if (target == null)
        {
            return CodexCommandResult.Fail("Could not find GameObject at path: " + hierarchyPath);
        }

        var componentType = ResolveType(componentTypeName);
        if (componentType == null)
        {
            return CodexCommandResult.Fail("Could not resolve component type: " + componentTypeName);
        }

        var component = target.GetComponent(componentType);
        if (component == null)
        {
            return CodexCommandResult.Fail("Component " + componentTypeName + " not found on " + hierarchyPath);
        }

        var serializedObject = new SerializedObject(component);
        var property = serializedObject.FindProperty(propertyName);
        if (property == null)
        {
            return CodexCommandResult.Fail("Serialized property not found: " + propertyName);
        }

        if (!TrySetSerializedProperty(property, propertyValue))
        {
            return CodexCommandResult.Fail("Unsupported property type or value format for: " + propertyName);
        }

        serializedObject.ApplyModifiedProperties();
        EditorUtility.SetDirty(component);
        return CodexCommandResult.Success("Updated " + componentTypeName + "." + propertyName + " on " + hierarchyPath);
    }

    public static CodexCommandResult GetComponentProperty(string hierarchyPath, string componentTypeName, string propertyName)
    {
        var target = FindTransform(hierarchyPath);
        if (target == null)
        {
            return CodexCommandResult.Fail("Could not find GameObject at path: " + hierarchyPath);
        }

        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return CodexCommandResult.Fail("Property name is required.");
        }

        var componentType = ResolveType(componentTypeName);
        if (componentType == null)
        {
            return CodexCommandResult.Fail("Could not resolve component type: " + componentTypeName);
        }

        var component = target.GetComponent(componentType);
        if (component == null)
        {
            return CodexCommandResult.Fail("Component " + componentTypeName + " not found on " + hierarchyPath);
        }

        var serializedObject = new SerializedObject(component);
        var property = serializedObject.FindProperty(propertyName);
        if (property == null)
        {
            return CodexCommandResult.Fail("Serialized property not found: " + propertyName);
        }

        string value;
        if (!TryReadSerializedProperty(property, out value))
        {
            return CodexCommandResult.Fail("Unsupported serialized property type for: " + propertyName);
        }

        return CodexCommandResult.Success("Property " + componentTypeName + "." + propertyName + " = " + value);
    }

    public static CodexCommandResult RemoveComponent(string hierarchyPath, string componentTypeName)
    {
        var target = FindTransform(hierarchyPath);
        if (target == null)
        {
            return CodexCommandResult.Fail("Could not find GameObject at path: " + hierarchyPath);
        }

        var componentType = ResolveType(componentTypeName);
        if (componentType == null)
        {
            return CodexCommandResult.Fail("Could not resolve component type: " + componentTypeName);
        }

        if (!typeof(Component).IsAssignableFrom(componentType))
        {
            return CodexCommandResult.Fail(componentTypeName + " is not a Unity Component type.");
        }

        if (componentType == typeof(Transform))
        {
            return CodexCommandResult.Fail("Cannot remove Transform component.");
        }

        var component = target.GetComponent(componentType);
        if (component == null)
        {
            return CodexCommandResult.Fail("Component " + componentTypeName + " not found on " + hierarchyPath);
        }

        Undo.DestroyObjectImmediate(component);
        return CodexCommandResult.Success("Removed component " + componentType.FullName + " from " + hierarchyPath);
    }

    public static CodexCommandResult SelectObject(string hierarchyPath)
    {
        var target = FindTransform(hierarchyPath);
        if (target == null)
        {
            return CodexCommandResult.Fail("Could not find GameObject at path: " + hierarchyPath);
        }

        Selection.activeGameObject = target.gameObject;
        EditorGUIUtility.PingObject(target.gameObject);
        return CodexCommandResult.Success("Selected GameObject: " + hierarchyPath);
    }

    public static CodexCommandResult SetTagAndLayer(string hierarchyPath, string tag, int layer)
    {
        var target = FindTransform(hierarchyPath);
        if (target == null)
        {
            return CodexCommandResult.Fail("Could not find GameObject at path: " + hierarchyPath);
        }

        Undo.RecordObject(target.gameObject, "Codex Set Tag/Layer");
        if (!string.IsNullOrWhiteSpace(tag))
        {
            target.gameObject.tag = tag;
        }

        if (layer >= 0 && layer <= 31)
        {
            target.gameObject.layer = layer;
        }

        EditorUtility.SetDirty(target.gameObject);
        return CodexCommandResult.Success("Updated tag/layer for: " + hierarchyPath);
    }

    public static CodexCommandResult CreateFolder(string parentAssetFolder, string newFolderName)
    {
        if (string.IsNullOrWhiteSpace(parentAssetFolder))
        {
            parentAssetFolder = "Assets";
        }

        if (string.IsNullOrWhiteSpace(newFolderName))
        {
            return CodexCommandResult.Fail("New folder name is required.");
        }

        var guid = AssetDatabase.CreateFolder(parentAssetFolder, newFolderName);
        if (string.IsNullOrWhiteSpace(guid))
        {
            return CodexCommandResult.Fail("Failed to create folder.");
        }

        var assetPath = AssetDatabase.GUIDToAssetPath(guid);
        return CodexCommandResult.Success("Created folder: " + assetPath);
    }


    public static CodexCommandResult MoveToParent(string hierarchyPath, string newParentHierarchyPath)
    {
        var target = FindTransform(hierarchyPath);
        if (target == null)
        {
            return CodexCommandResult.Fail("Could not find GameObject at path: " + hierarchyPath);
        }

        Transform parent = null;
        if (!string.IsNullOrWhiteSpace(newParentHierarchyPath))
        {
            parent = FindTransform(newParentHierarchyPath);
            if (parent == null)
            {
                return CodexCommandResult.Fail("Could not find parent GameObject at path: " + newParentHierarchyPath);
            }
        }

        Undo.SetTransformParent(target, parent, "Codex Move To Parent");
        EditorUtility.SetDirty(target);
        return CodexCommandResult.Success("Moved object to parent. New path: " + GetHierarchyPath(target));
    }

    public static CodexCommandResult ListRootObjects()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var roots = scene.GetRootGameObjects();
        if (roots == null || roots.Length == 0)
        {
            return CodexCommandResult.Success("No root objects in active scene.");
        }

        var names = new string[roots.Length];
        for (var i = 0; i < roots.Length; i++)
        {
            names[i] = roots[i].name;
        }

        return CodexCommandResult.Success("Root objects: " + string.Join(", ", names));
    }

    public static CodexCommandResult ListChildren(string hierarchyPath)
    {
        var target = FindTransform(hierarchyPath);
        if (target == null)
        {
            return CodexCommandResult.Fail("Could not find GameObject at path: " + hierarchyPath);
        }

        if (target.childCount == 0)
        {
            return CodexCommandResult.Success("No children under: " + hierarchyPath);
        }

        var names = new string[target.childCount];
        for (var i = 0; i < target.childCount; i++)
        {
            names[i] = target.GetChild(i).name;
        }

        return CodexCommandResult.Success("Children of " + hierarchyPath + ": " + string.Join(", ", names));
    }

    public static CodexCommandResult CreateMaterial(string assetPath, string shaderName)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            return CodexCommandResult.Fail("Material asset path is required.");
        }

        if (string.IsNullOrWhiteSpace(shaderName))
        {
            shaderName = "Standard";
        }

        var shader = Shader.Find(shaderName);
        if (shader == null)
        {
            return CodexCommandResult.Fail("Could not find shader: " + shaderName);
        }

        var material = new Material(shader);
        AssetDatabase.CreateAsset(material, assetPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return CodexCommandResult.Success("Created material: " + assetPath);
    }

    public static CodexCommandResult AssignMaterial(string hierarchyPath, string materialAssetPath)
    {
        var target = FindTransform(hierarchyPath);
        if (target == null)
        {
            return CodexCommandResult.Fail("Could not find GameObject at path: " + hierarchyPath);
        }

        var renderer = target.GetComponent<Renderer>();
        if (renderer == null)
        {
            return CodexCommandResult.Fail("No Renderer found on: " + hierarchyPath);
        }

        var material = AssetDatabase.LoadAssetAtPath<Material>(materialAssetPath);
        if (material == null)
        {
            return CodexCommandResult.Fail("Could not load material at path: " + materialAssetPath);
        }

        Undo.RecordObject(renderer, "Codex Assign Material");
        renderer.sharedMaterial = material;
        EditorUtility.SetDirty(renderer);
        return CodexCommandResult.Success("Assigned material to: " + hierarchyPath);
    }

    public static CodexCommandResult FocusSceneView(string hierarchyPath)
    {
        var target = FindTransform(hierarchyPath);
        if (target == null)
        {
            return CodexCommandResult.Fail("Could not find GameObject at path: " + hierarchyPath);
        }

        Selection.activeGameObject = target.gameObject;
        SceneView.lastActiveSceneView?.FrameSelected();
        return CodexCommandResult.Success("Focused Scene view on: " + hierarchyPath);
    }

    public static CodexCommandResult RunMenuItem(string menuPath)
    {
        if (string.IsNullOrWhiteSpace(menuPath))
        {
            return CodexCommandResult.Fail("Menu path is required.");
        }

        var executed = EditorApplication.ExecuteMenuItem(menuPath);
        if (!executed)
        {
            return CodexCommandResult.Fail("Menu item did not execute: " + menuPath);
        }

        return CodexCommandResult.Success("Executed menu item: " + menuPath);
    }

    public static CodexCommandResult SetPlayMode(bool isPlaying)
    {
        EditorApplication.isPlaying = isPlaying;
        return CodexCommandResult.Success(isPlaying ? "Entered play mode." : "Exited play mode.");
    }


    public static CodexCommandResult CaptureSceneViewScreenshot(string outputPath, int width = 1280, int height = 720)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return CodexCommandResult.Fail("Screenshot output path is required.");
        }

        var sceneView = SceneView.lastActiveSceneView;
        if (sceneView == null || sceneView.camera == null)
        {
            return CodexCommandResult.Fail("No active SceneView camera available for screenshot.");
        }

        var fullPath = ResolveOutputPath(outputPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var renderTexture = new RenderTexture(width, height, 24);
        var texture = new Texture2D(width, height, TextureFormat.RGB24, false);

        var camera = sceneView.camera;
        var previousTarget = camera.targetTexture;
        var previousActive = RenderTexture.active;

        camera.targetTexture = renderTexture;
        camera.Render();
        RenderTexture.active = renderTexture;
        texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        texture.Apply();

        camera.targetTexture = previousTarget;
        RenderTexture.active = previousActive;

        var bytes = texture.EncodeToPNG();
        File.WriteAllBytes(fullPath, bytes);

        UnityEngine.Object.DestroyImmediate(renderTexture);
        UnityEngine.Object.DestroyImmediate(texture);

        AssetDatabase.Refresh();
        return CodexCommandResult.Success("Captured SceneView screenshot: " + fullPath);
    }

    public static CodexCommandResult CaptureGameViewScreenshot(string outputPath, int superSize = 1)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return CodexCommandResult.Fail("Screenshot output path is required.");
        }

        var fullPath = ResolveOutputPath(outputPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        ScreenCapture.CaptureScreenshot(fullPath, Mathf.Max(1, superSize));
        AssetDatabase.Refresh();
        return CodexCommandResult.Success("Captured GameView screenshot: " + fullPath);
    }


    public static CodexCommandResult GetTransformInfo(string hierarchyPath)
    {
        var target = FindTransform(hierarchyPath);
        if (target == null)
        {
            return CodexCommandResult.Fail("Could not find GameObject at path: " + hierarchyPath);
        }

        var p = target.localPosition;
        var r = target.localEulerAngles;
        var s = target.localScale;
        return CodexCommandResult.Success(
            "Transform " + hierarchyPath + " | pos=(" + p.x.ToString("F3") + "," + p.y.ToString("F3") + "," + p.z.ToString("F3")
            + ") rot=(" + r.x.ToString("F3") + "," + r.y.ToString("F3") + "," + r.z.ToString("F3")
            + ") scale=(" + s.x.ToString("F3") + "," + s.y.ToString("F3") + "," + s.z.ToString("F3") + ")");
    }

    public static CodexCommandResult ListComponents(string hierarchyPath)
    {
        var target = FindTransform(hierarchyPath);
        if (target == null)
        {
            return CodexCommandResult.Fail("Could not find GameObject at path: " + hierarchyPath);
        }

        var components = target.GetComponents<Component>();
        if (components == null || components.Length == 0)
        {
            return CodexCommandResult.Success("No components found on: " + hierarchyPath);
        }

        var names = new string[components.Length];
        for (var i = 0; i < components.Length; i++)
        {
            names[i] = components[i] != null ? components[i].GetType().FullName : "<missing>";
        }

        return CodexCommandResult.Success("Components on " + hierarchyPath + ": " + string.Join(", ", names));
    }

    public static CodexCommandResult FindObjectsByName(string nameContains)
    {
        if (string.IsNullOrWhiteSpace(nameContains))
        {
            return CodexCommandResult.Fail("nameContains is required.");
        }

        var activeScene = EditorSceneManager.GetActiveScene();
        var roots = activeScene.GetRootGameObjects();
        var matches = new System.Collections.Generic.List<string>();

        for (var i = 0; i < roots.Length; i++)
        {
            FindMatchesRecursive(roots[i].transform, nameContains, matches);
        }

        if (matches.Count == 0)
        {
            return CodexCommandResult.Success("No objects found containing name: " + nameContains);
        }

        return CodexCommandResult.Success("Found objects: " + string.Join(" | ", matches));
    }

    public static CodexCommandResult FindObjectsByComponent(string componentTypeName)
    {
        var componentType = ResolveType(componentTypeName);
        if (componentType == null)
        {
            return CodexCommandResult.Fail("Could not resolve component type: " + componentTypeName);
        }

        if (!typeof(Component).IsAssignableFrom(componentType))
        {
            return CodexCommandResult.Fail(componentTypeName + " is not a Unity Component type.");
        }

        var activeScene = EditorSceneManager.GetActiveScene();
        var roots = activeScene.GetRootGameObjects();
        var matches = new System.Collections.Generic.List<string>();

        for (var i = 0; i < roots.Length; i++)
        {
            FindObjectsByComponentRecursive(roots[i].transform, componentType, matches);
        }

        if (matches.Count == 0)
        {
            return CodexCommandResult.Success("No objects found with component: " + componentTypeName);
        }

        return CodexCommandResult.Success("Objects with " + componentTypeName + ": " + string.Join(" | ", matches));
    }

    public static CodexCommandResult ListSceneHierarchy(int maxDepth = 8)
    {
        var activeScene = EditorSceneManager.GetActiveScene();
        var roots = activeScene.GetRootGameObjects();
        if (roots == null || roots.Length == 0)
        {
            return CodexCommandResult.Success("No root objects in active scene.");
        }

        maxDepth = Mathf.Clamp(maxDepth, 1, 32);
        const int maxNodes = 500;
        var nodes = 0;
        var lines = new System.Collections.Generic.List<string>();
        for (var i = 0; i < roots.Length; i++)
        {
            AppendHierarchyLines(roots[i].transform, 0, maxDepth, lines, ref nodes, maxNodes);
            if (nodes >= maxNodes)
            {
                break;
            }
        }

        if (nodes >= maxNodes)
        {
            lines.Add("... output truncated at " + maxNodes + " nodes.");
        }

        return CodexCommandResult.Success("Scene hierarchy:\n" + string.Join("\n", lines));
    }

    public static CodexCommandResult CreatePrefabFromObject(string hierarchyPath, string prefabAssetPath)
    {
        var target = FindTransform(hierarchyPath);
        if (target == null)
        {
            return CodexCommandResult.Fail("Could not find GameObject at path: " + hierarchyPath);
        }

        if (string.IsNullOrWhiteSpace(prefabAssetPath))
        {
            return CodexCommandResult.Fail("prefabAssetPath is required.");
        }

        var directory = Path.GetDirectoryName(prefabAssetPath);
        if (!string.IsNullOrWhiteSpace(directory) && !AssetDatabase.IsValidFolder(directory))
        {
            return CodexCommandResult.Fail("Target prefab directory does not exist: " + directory);
        }

        var prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(target.gameObject, prefabAssetPath, InteractionMode.UserAction);
        if (prefab == null)
        {
            return CodexCommandResult.Fail("Failed to save prefab: " + prefabAssetPath);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return CodexCommandResult.Success("Saved prefab: " + prefabAssetPath);
    }

    public static CodexCommandResult SaveProject()
    {
        EditorSceneManager.SaveOpenScenes();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return CodexCommandResult.Success("Saved open scenes and assets.");
    }

    public static CodexCommandResult SaveActiveScene()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid())
        {
            return CodexCommandResult.Fail("No active scene to save.");
        }

        var saved = EditorSceneManager.SaveScene(scene);
        if (!saved)
        {
            return CodexCommandResult.Fail("Failed to save active scene.");
        }

        return CodexCommandResult.Success("Saved active scene: " + scene.path);
    }

    public static CodexCommandResult GetActiveSceneInfo()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid())
        {
            return CodexCommandResult.Fail("No active scene.");
        }

        var rootCount = scene.rootCount;
        var path = string.IsNullOrWhiteSpace(scene.path) ? "<Unsaved Scene>" : scene.path;
        return CodexCommandResult.Success(
            "Active scene: name=" + scene.name + ", path=" + path + ", isDirty=" + scene.isDirty + ", rootCount=" + rootCount);
    }

    public static CodexCommandResult MarkActiveSceneDirty()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid())
        {
            return CodexCommandResult.Fail("No active scene to mark dirty.");
        }

        EditorSceneManager.MarkSceneDirty(scene);
        return CodexCommandResult.Success("Marked active scene dirty: " + scene.path);
    }

    public static CodexCommandResult ListOpenScenes()
    {
        var count = EditorSceneManager.sceneCount;
        if (count == 0)
        {
            return CodexCommandResult.Success("No open scenes.");
        }

        var sceneNames = new string[count];
        for (var i = 0; i < count; i++)
        {
            var scene = EditorSceneManager.GetSceneAt(i);
            sceneNames[i] = string.IsNullOrWhiteSpace(scene.path) ? "<Unsaved Scene>" : scene.path;
        }

        return CodexCommandResult.Success("Open scenes: " + string.Join(" | ", sceneNames));
    }

    public static CodexCommandResult FindMissingScripts()
    {
        var activeScene = EditorSceneManager.GetActiveScene();
        var roots = activeScene.GetRootGameObjects();
        var missingScriptObjects = new System.Collections.Generic.List<string>();

        for (var i = 0; i < roots.Length; i++)
        {
            FindMissingScriptsRecursive(roots[i].transform, missingScriptObjects);
        }

        if (missingScriptObjects.Count == 0)
        {
            return CodexCommandResult.Success("No objects with missing scripts found in active scene.");
        }

        return CodexCommandResult.Success("Objects with missing scripts: " + string.Join(" | ", missingScriptObjects));
    }

    public static CodexCommandResult SelectAsset(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            return CodexCommandResult.Fail("assetPath is required.");
        }

        var asset = AssetDatabase.LoadMainAssetAtPath(assetPath);
        if (asset == null)
        {
            return CodexCommandResult.Fail("Could not load asset at path: " + assetPath);
        }

        Selection.activeObject = asset;
        EditorGUIUtility.PingObject(asset);
        return CodexCommandResult.Success("Selected asset: " + assetPath);
    }

    public static CodexCommandResult UndoLastAction()
    {
        Undo.PerformUndo();
        return CodexCommandResult.Success("Performed undo.");
    }

    public static CodexCommandResult RedoLastAction()
    {
        Undo.PerformRedo();
        return CodexCommandResult.Success("Performed redo.");
    }

    public static string[] GetCapabilities()
    {
        return new[]
        {
            "open_scene(scenePath)",
            "open_scene_additive(scenePath)",
            "create_scene_asset(sceneName,sceneFolder)",
            "close_scene(scenePath,removeSceneObjects)",
            "create_gameobject(name,parentPath)",
            "create_primitive(primitiveType,name,parentPath)",
            "delete_gameobject(path)",
            "duplicate_gameobject(path)",
            "rename_gameobject(path,newName)",
            "set_active(path,isActive)",
            "instantiate_prefab(prefabAssetPath,parentPath)",
            "set_transform(path,position,rotation,scale)",
            "reset_transform(path)",
            "add_component(path,componentType)",
            "set_component_property(path,componentType,propertyName,propertyValue)",
            "get_component_property(path,componentType,propertyName)",
            "remove_component(path,componentType)",
            "select_object(path)",
            "set_tag_layer(path,tag,layer)",
            "create_folder(parentAssetFolder,newFolderName)",
            "move_to_parent(path,newParentPath)",
            "list_root_objects()",
            "list_children(path)",
            "create_material(assetPath,shaderName)",
            "assign_material(path,materialAssetPath)",
            "focus_scene_view(path)",
            "run_menu_item(menuPath)",
            "set_play_mode(isPlaying)",
            "capture_scene_view_screenshot(outputPath,width,height)",
            "capture_game_view_screenshot(outputPath,superSize)",
            "get_transform_info(path)",
            "list_components(path)",
            "find_objects_by_name(nameContains)",
            "find_objects_by_component(componentType)",
            "list_scene_hierarchy(maxDepth)",
            "create_prefab_from_object(path,prefabAssetPath)",
            "list_open_scenes()",
            "find_missing_scripts()",
            "select_asset(assetPath)",
            "get_active_scene_info()",
            "save_active_scene()",
            "mark_scene_dirty()",
            "undo_last()",
            "redo_last()",
            "save_project()"
        };
    }

    private static Transform FindTransform(string hierarchyPath)
    {
        if (string.IsNullOrWhiteSpace(hierarchyPath))
        {
            return null;
        }

        var activeScene = EditorSceneManager.GetActiveScene();
        var roots = activeScene.GetRootGameObjects();
        for (var i = 0; i < roots.Length; i++)
        {
            var root = roots[i].transform;
            if (string.Equals(root.name, hierarchyPath, StringComparison.Ordinal))
            {
                return root;
            }

            var split = hierarchyPath.Split('/');
            if (split.Length > 0 && split[0] == root.name)
            {
                var current = root;
                var found = true;
                for (var j = 1; j < split.Length; j++)
                {
                    current = current.Find(split[j]);
                    if (current == null)
                    {
                        found = false;
                        break;
                    }
                }

                if (found)
                {
                    return current;
                }
            }
        }

        return null;
    }

    private static string GetHierarchyPath(Transform transform)
    {
        if (transform == null)
        {
            return string.Empty;
        }

        var path = transform.name;
        var current = transform.parent;
        while (current != null)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }

        return path;
    }

    private static Type ResolveType(string typeName)
    {
        var type = Type.GetType(typeName, false);
        if (type != null)
        {
            return type;
        }

        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (var i = 0; i < assemblies.Length; i++)
        {
            type = assemblies[i].GetType(typeName, false);
            if (type != null)
            {
                return type;
            }
        }

        return null;
    }



    private static void FindMatchesRecursive(Transform transform, string token, System.Collections.Generic.List<string> matches)
    {
        if (transform.name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            matches.Add(GetHierarchyPath(transform));
        }

        for (var i = 0; i < transform.childCount; i++)
        {
            FindMatchesRecursive(transform.GetChild(i), token, matches);
        }
    }

    private static void FindObjectsByComponentRecursive(Transform transform, Type componentType, System.Collections.Generic.List<string> matches)
    {
        if (transform.GetComponent(componentType) != null)
        {
            matches.Add(GetHierarchyPath(transform));
        }

        for (var i = 0; i < transform.childCount; i++)
        {
            FindObjectsByComponentRecursive(transform.GetChild(i), componentType, matches);
        }
    }

    private static void AppendHierarchyLines(
        Transform transform,
        int depth,
        int maxDepth,
        System.Collections.Generic.List<string> lines,
        ref int nodeCount,
        int maxNodes)
    {
        if (depth > maxDepth || nodeCount >= maxNodes)
        {
            return;
        }

        lines.Add(new string(' ', depth * 2) + "- " + transform.name);
        nodeCount++;
        for (var i = 0; i < transform.childCount; i++)
        {
            AppendHierarchyLines(transform.GetChild(i), depth + 1, maxDepth, lines, ref nodeCount, maxNodes);
            if (nodeCount >= maxNodes)
            {
                return;
            }
        }
    }

    private static void FindMissingScriptsRecursive(Transform transform, System.Collections.Generic.List<string> matches)
    {
        var components = transform.GetComponents<Component>();
        for (var i = 0; i < components.Length; i++)
        {
            if (components[i] == null)
            {
                matches.Add(GetHierarchyPath(transform));
                break;
            }
        }

        for (var i = 0; i < transform.childCount; i++)
        {
            FindMissingScriptsRecursive(transform.GetChild(i), matches);
        }
    }

    private static string ResolveOutputPath(string outputPath)
    {
        if (Path.IsPathRooted(outputPath))
        {
            return outputPath;
        }

        return Path.Combine(Directory.GetCurrentDirectory(), outputPath);
    }

    private static bool TrySetSerializedProperty(SerializedProperty property, string value)
    {
        switch (property.propertyType)
        {
            case SerializedPropertyType.Integer:
                int intValue;
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out intValue))
                {
                    return false;
                }

                property.intValue = intValue;
                return true;

            case SerializedPropertyType.Boolean:
                bool boolValue;
                if (!bool.TryParse(value, out boolValue))
                {
                    return false;
                }

                property.boolValue = boolValue;
                return true;

            case SerializedPropertyType.Float:
                float floatValue;
                if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out floatValue))
                {
                    return false;
                }

                property.floatValue = floatValue;
                return true;

            case SerializedPropertyType.String:
                property.stringValue = value;
                return true;

            default:
                return false;
        }
    }

    private static bool TryReadSerializedProperty(SerializedProperty property, out string value)
    {
        switch (property.propertyType)
        {
            case SerializedPropertyType.Integer:
                value = property.intValue.ToString(CultureInfo.InvariantCulture);
                return true;
            case SerializedPropertyType.Boolean:
                value = property.boolValue ? "true" : "false";
                return true;
            case SerializedPropertyType.Float:
                value = property.floatValue.ToString(CultureInfo.InvariantCulture);
                return true;
            case SerializedPropertyType.String:
                value = property.stringValue;
                return true;
            case SerializedPropertyType.Vector2:
                value = property.vector2Value.ToString("F3", CultureInfo.InvariantCulture);
                return true;
            case SerializedPropertyType.Vector3:
                value = property.vector3Value.ToString("F3", CultureInfo.InvariantCulture);
                return true;
            case SerializedPropertyType.Enum:
                value = property.enumDisplayNames[property.enumValueIndex];
                return true;
            default:
                value = string.Empty;
                return false;
        }
    }

    private static bool TryParsePrimitiveType(string primitiveTypeName, out PrimitiveType primitiveType)
    {
        primitiveType = PrimitiveType.Cube;
        if (string.IsNullOrWhiteSpace(primitiveTypeName))
        {
            primitiveType = PrimitiveType.Cube;
            return true;
        }

        foreach (PrimitiveType value in Enum.GetValues(typeof(PrimitiveType)))
        {
            if (string.Equals(value.ToString(), primitiveTypeName, StringComparison.OrdinalIgnoreCase))
            {
                primitiveType = value;
                return true;
            }
        }

        return false;
    }
}

public struct CodexCommandResult
{
    public bool Ok;
    public string Message;

    public static CodexCommandResult Success(string message)
    {
        return new CodexCommandResult
        {
            Ok = true,
            Message = message
        };
    }

    public static CodexCommandResult Fail(string message)
    {
        return new CodexCommandResult
        {
            Ok = false,
            Message = message
        };
    }
}
