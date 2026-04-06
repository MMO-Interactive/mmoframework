using UnityEngine;
using UnityEngine.SceneManagement;

namespace MMONetworking.Client
{
public static class UnityMmoSceneInstaller
{
    private static bool _initialized;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        InstallForScene(SceneManager.GetActiveScene().name);
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        InstallForScene(scene.name);
    }

    private static void InstallForScene(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            return;
        }

        if (sceneName == UnityMmoSceneNames.Login)
        {
            EnsureController<UnityMmoLoginSceneController>("Login Scene Controller");
            return;
        }

        if (sceneName == UnityMmoSceneNames.CharacterCreation)
        {
            EnsureController<UnityMmoCharacterCreationSceneController>("Character Creation Scene Controller");
            return;
        }

        if (sceneName == UnityMmoSceneNames.CharacterSelection)
        {
            EnsureController<UnityMmoCharacterSelectionSceneController>("Character Selection Scene Controller");
            return;
        }

        if (sceneName == UnityMmoSceneNames.World || sceneName == "SampleScene")
        {
            EnsureController<UnityMmoWorldSceneController>("World Scene Controller");
        }
    }

    private static void EnsureController<T>(string objectName) where T : Component
    {
        if (Object.FindObjectOfType<T>() != null)
        {
            return;
        }

        var root = new GameObject(objectName);
        root.AddComponent<T>();
    }
}
}
