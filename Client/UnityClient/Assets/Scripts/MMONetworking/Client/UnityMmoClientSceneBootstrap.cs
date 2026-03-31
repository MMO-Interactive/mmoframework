using UnityEngine;
using UnityEngine.SceneManagement;

namespace MMONetworking.Client
{
public static class UnityMmoClientSceneBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (Object.FindObjectOfType<UnityMmoClient>() != null)
        {
            return;
        }

        var root = new GameObject("MMO Client Runtime");
        var client = root.AddComponent<UnityMmoClient>();
        root.AddComponent<UnityMmoClientStatusView>();
        root.AddComponent<UnityMmoRemoteAvatarSystem>();
        root.AddComponent<UnityMmoResourceNodeSystem>();
        root.AddComponent<UnityMmoMobSystem>();

        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Arena Floor";
        floor.transform.position = Vector3.zero;
        floor.transform.localScale = new Vector3(8f, 1f, 8f);
        var floorRenderer = floor.GetComponent<Renderer>();
        if (floorRenderer != null)
        {
            UnityMmoMaterialFactory.Apply(floorRenderer, new Color(0.84f, 0.79f, 0.68f));
        }

        var avatar = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        avatar.name = "Local Player Avatar";
        avatar.transform.position = new Vector3(5f, 1f, 5f);
        var avatarRenderer = avatar.GetComponent<Renderer>();
        if (avatarRenderer != null)
        {
            UnityMmoMaterialFactory.Apply(avatarRenderer, new Color(0.15f, 0.49f, 0.78f));
        }

        var avatarPresenter = avatar.AddComponent<UnityMmoClientAvatar>();
        avatarPresenter.Bind(client);

        var camera = Camera.main;
        if (camera == null)
        {
            var cameraObject = new GameObject("Main Camera");
            camera = cameraObject.AddComponent<Camera>();
            camera.tag = "MainCamera";
            cameraObject.AddComponent<AudioListener>();
        }

        var follow = camera.GetComponent<UnityMmoCameraFollow>();
        if (follow == null)
        {
            follow = camera.gameObject.AddComponent<UnityMmoCameraFollow>();
        }

        follow.Bind(avatar.transform);
        camera.transform.position = avatar.transform.position + new Vector3(0f, 9f, -11f);
        camera.transform.LookAt(avatar.transform.position + new Vector3(0f, 1.25f, 0f));

        if (Object.FindObjectOfType<Light>() == null)
        {
            var lightObject = new GameObject("Directional Light");
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        Debug.Log("Unity client scene bootstrap created runtime MMO objects in scene " + SceneManager.GetActiveScene().name + ".");
    }
}
}
