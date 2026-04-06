using UnityEngine;

namespace MMONetworking.Client
{
public static class UnityMmoClientAppBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void EnsureApp()
    {
        if (Object.FindObjectOfType<UnityMmoClient>() != null)
        {
            return;
        }

        var root = new GameObject("MMO Client App");
        Object.DontDestroyOnLoad(root);
        root.AddComponent<UnityMmoAssetBundleService>();
        root.AddComponent<UnityMmoClient>();
    }
}
}
