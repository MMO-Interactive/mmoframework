using System;
using System.IO;
using System.Linq;
using RiseOfHeroes.WorldEditor.Authoring;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RiseOfHeroes.WorldEditor
{
public static class WorldEditorAssetBundleExporter
{
    private const string DefaultBundlePrefix = "zone-";
    
    public sealed class BuildResult
    {
        public string OutputPath { get; set; }
        public string AuthoringOutputPath { get; set; }
        public string[] BundleNames { get; set; } = Array.Empty<string>();
    }

    [MenuItem("Rise of Heroes/Asset Bundles/Assign Active Scene As Zone Bundle", priority = 100)]
    public static void AssignActiveSceneAsZoneBundle()
    {
        var metadata = UnityEngine.Object.FindFirstObjectByType<ZoneSceneMetadata>();
        var preferredBundleName = metadata != null && !string.IsNullOrWhiteSpace(metadata.AssetBundleName)
            ? metadata.AssetBundleName.Trim()
            : null;
        AssignActiveSceneAsZoneBundle(preferredBundleName);
    }

    public static void AssignActiveSceneAsZoneBundle(string explicitBundleName)
    {
        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || string.IsNullOrWhiteSpace(scene.path))
        {
            EditorUtility.DisplayDialog("Assign Zone Bundle", "Open and save a scene first.", "OK");
            return;
        }

        var importer = AssetImporter.GetAtPath(scene.path);
        if (importer == null)
        {
            EditorUtility.DisplayDialog("Assign Zone Bundle", "Could not locate the active scene asset importer.", "OK");
            return;
        }

        var bundleName = string.IsNullOrWhiteSpace(explicitBundleName)
            ? DefaultBundlePrefix + SanitizeName(scene.name)
            : explicitBundleName.Trim();
        importer.assetBundleName = bundleName;
        importer.assetBundleVariant = string.Empty;
        importer.SaveAndReimport();
        AssetDatabase.RemoveUnusedAssetBundleNames();

        Debug.Log("Assigned active scene '" + scene.name + "' to asset bundle '" + bundleName + "'.");
        EditorUtility.DisplayDialog("Assign Zone Bundle", "Assigned '" + scene.name + "' to '" + bundleName + "'.", "OK");
    }

    [MenuItem("Rise of Heroes/Asset Bundles/Build Bundles For Active Target", priority = 110)]
    public static void BuildBundlesForActiveTarget()
    {
        BuildBundlesAndShowDialog(EditorUserBuildSettings.activeBuildTarget);
    }

    [MenuItem("Rise of Heroes/Asset Bundles/Build Windows Bundles", priority = 111)]
    public static void BuildWindowsBundles()
    {
        BuildBundlesAndShowDialog(BuildTarget.StandaloneWindows64);
    }

    [MenuItem("Rise of Heroes/Asset Bundles/Open Output Folder", priority = 120)]
    public static void OpenOutputFolder()
    {
        var outputPath = ResolveOutputDirectory(EditorUserBuildSettings.activeBuildTarget);
        Directory.CreateDirectory(outputPath);
        EditorUtility.RevealInFinder(outputPath);
    }

    public static void BuildBundlesFromCommandLine()
    {
        BuildBundles(EditorUserBuildSettings.activeBuildTarget);
    }

    public static BuildResult BuildBundles(BuildTarget buildTarget)
    {
        var bundleNames = AssetDatabase.GetAllAssetBundleNames();
        if (bundleNames == null || bundleNames.Length == 0)
        {
            throw new InvalidOperationException("No asset bundles are assigned. Use 'Assign Active Scene As Zone Bundle' first.");
        }

        var outputPath = ResolveOutputDirectory(buildTarget);
        Directory.CreateDirectory(outputPath);

        var manifest = BuildPipeline.BuildAssetBundles(
            outputPath,
            BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode,
            buildTarget);

        if (manifest == null)
        {
            throw new InvalidOperationException("Unity returned a null AssetBundleManifest.");
        }

        WriteBuildSummary(outputPath, buildTarget, manifest);
        var authoringOutputPath = Path.Combine(outputPath, SceneManager.GetActiveScene().name + ".authoring.json");
        WorldEditorSceneExportUtility.ExportActiveSceneAuthoringJson(authoringOutputPath);
        AssetDatabase.Refresh();

        return new BuildResult
        {
            OutputPath = outputPath,
            AuthoringOutputPath = authoringOutputPath,
            BundleNames = manifest.GetAllAssetBundles().OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }

    private static void BuildBundlesAndShowDialog(BuildTarget buildTarget)
    {
        var result = BuildBundles(buildTarget);
        var message =
            "Built " + result.BundleNames.Length + " bundle(s) to:\n" +
            result.OutputPath + "\n\nAuthoring JSON:\n" + result.AuthoringOutputPath;
        Debug.Log(message);
        EditorUtility.DisplayDialog("Build Asset Bundles", message, "OK");
    }

    private static void WriteBuildSummary(string outputPath, BuildTarget buildTarget, AssetBundleManifest manifest)
    {
        var bundleLines = manifest
            .GetAllAssetBundles()
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Select(name =>
            {
                var fullPath = Path.Combine(outputPath, name);
                var sizeBytes = File.Exists(fullPath) ? new FileInfo(fullPath).Length : 0L;
                return "  {\n" +
                       "    \"bundleName\": \"" + EscapeJson(name) + "\",\n" +
                       "    \"sizeBytes\": " + sizeBytes + ",\n" +
                       "    \"dependencies\": [" + string.Join(", ", manifest.GetAllDependencies(name).Select(dep => "\"" + EscapeJson(dep) + "\"")) + "]\n" +
                       "  }";
            })
            .ToArray();

        var summary =
            "{\n" +
            "  \"generatedAtUtc\": \"" + DateTime.UtcNow.ToString("O") + "\",\n" +
            "  \"buildTarget\": \"" + buildTarget + "\",\n" +
            "  \"unityVersion\": \"" + Application.unityVersion + "\",\n" +
            "  \"outputPath\": \"" + EscapeJson(outputPath.Replace('\\', '/')) + "\",\n" +
            "  \"bundles\": [\n" + string.Join(",\n", bundleLines) + "\n  ]\n" +
            "}\n";

        File.WriteAllText(Path.Combine(outputPath, "worldeditor-bundle-build.json"), summary);
    }

    private static string ResolveOutputDirectory(BuildTarget buildTarget)
    {
        var workspaceRoot = ResolveWorkspaceRoot();
        return Path.Combine(workspaceRoot, "Builds", "AssetBundles", "WorldEditor", buildTarget.ToString());
    }

    private static string ResolveWorkspaceRoot()
    {
        var current = new DirectoryInfo(Path.GetFullPath(Path.Combine(Application.dataPath, "..")));
        while (current != null)
        {
            var hasServer = Directory.Exists(Path.Combine(current.FullName, "Server"));
            var hasClient = Directory.Exists(Path.Combine(current.FullName, "Client"));
            var hasShared = Directory.Exists(Path.Combine(current.FullName, "Shared"));
            if (hasServer && hasClient && hasShared)
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    }

    private static string SanitizeName(string value)
    {
        var chars = (value ?? string.Empty)
            .Trim()
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray();
        var next = new string(chars);
        while (next.Contains("--"))
        {
            next = next.Replace("--", "-");
        }

        return next.Trim('-');
    }

    private static string EscapeJson(string value)
        => (value ?? string.Empty)
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"");
}
}
