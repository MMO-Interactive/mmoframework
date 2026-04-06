using UnityEngine;
using UnityEngine.SceneManagement;

namespace MMONetworking.Client
{
public sealed class UnityMmoWorldSceneController : MonoBehaviour
{
    private const string FallbackFloorName = "Arena Floor";
    private const string FallbackLightName = "Directional Light";

    private void Awake()
    {
        var client = FindObjectOfType<UnityMmoClient>();
        if (client == null || !client.IsConnected)
        {
            SceneManager.LoadScene(UnityMmoSceneNames.Login);
            return;
        }

        EnsureWorldObjects();
        RefreshFallbackWorldPresentation();
    }

    private static void EnsureWorldObjects()
    {
        var client = Object.FindObjectOfType<UnityMmoClient>();
        if (client == null)
        {
            return;
        }

        if (Object.FindObjectOfType<UnityMmoClientStatusView>() == null)
        {
            var hudRoot = new GameObject("World HUD");
            hudRoot.AddComponent<UnityMmoClientStatusView>();
        }

        if (Object.FindObjectOfType<UnityMmoRemoteAvatarSystem>() == null)
        {
            var remoteRoot = new GameObject("Remote Avatar System");
            remoteRoot.AddComponent<UnityMmoRemoteAvatarSystem>();
        }

        if (Object.FindObjectOfType<UnityMmoResourceNodeSystem>() == null)
        {
            var nodeRoot = new GameObject("Resource Node System");
            nodeRoot.AddComponent<UnityMmoResourceNodeSystem>();
        }

        if (Object.FindObjectOfType<UnityMmoMobSystem>() == null)
        {
            var mobRoot = new GameObject("Mob System");
            mobRoot.AddComponent<UnityMmoMobSystem>();
        }

        if (Object.FindObjectOfType<UnityMmoNpcSystem>() == null)
        {
            var npcRoot = new GameObject("NPC System");
            npcRoot.AddComponent<UnityMmoNpcSystem>();
        }

        if (Object.FindObjectOfType<UnityMmoGameplayPanel>() == null)
        {
            var gameplayRoot = new GameObject("Gameplay Panel");
            gameplayRoot.AddComponent<UnityMmoGameplayPanel>();
        }

        var gameplayPanel = Object.FindObjectOfType<UnityMmoGameplayPanel>();
        if (gameplayPanel != null)
        {
            gameplayPanel.SetCollapsed(true);
        }

        if (Object.FindObjectOfType<UnityMmoSpellEffectSystem>() == null)
        {
            var spellEffectsRoot = new GameObject("Spell Effect System");
            spellEffectsRoot.AddComponent<UnityMmoSpellEffectSystem>();
        }

        if (Object.FindObjectOfType<UnityMmoWorldInteractionController>() == null)
        {
            var interactionRoot = new GameObject("World Interaction");
            interactionRoot.AddComponent<UnityMmoWorldInteractionController>();
        }

        if (Object.FindObjectOfType<UnityMmoMinimapView>() == null)
        {
            var minimapRoot = new GameObject("Minimap View");
            minimapRoot.AddComponent<UnityMmoMinimapView>();
        }

        if (Object.FindObjectOfType<UnityMmoWorldTrackerView>() == null)
        {
            var trackerRoot = new GameObject("World Tracker");
            trackerRoot.AddComponent<UnityMmoWorldTrackerView>();
        }

        var floor = GameObject.Find(FallbackFloorName);
        if (floor == null && !HasLoadedZonePresentationScene())
        {
            floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = FallbackFloorName;
            floor.transform.position = Vector3.zero;
            floor.transform.localScale = new Vector3(8f, 1f, 8f);
            var floorRenderer = floor.GetComponent<Renderer>();
            if (floorRenderer != null)
            {
                UnityMmoMaterialFactory.Apply(floorRenderer, new Color(0.84f, 0.79f, 0.68f));
            }
        }

        var avatar = GameObject.Find("Local Player Avatar");
        if (avatar == null)
        {
            if (!UnityMmoUmaAvatarFactory.TryCreateAvatar("Local Player Avatar", client.PlayerId, out avatar))
            {
                avatar = CreateFallbackLocalAvatar();
            }
        }

        if (avatar == null)
        {
            avatar = CreateFallbackLocalAvatar();
        }

        avatar.transform.position = client.AuthoritativePosition;

        var avatarPresenter = avatar.GetComponent<UnityMmoClientAvatar>();
        if (avatarPresenter == null)
        {
            avatarPresenter = avatar.AddComponent<UnityMmoClientAvatar>();
        }

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

        if (Object.FindObjectOfType<Light>() == null && !HasLoadedZonePresentationScene())
        {
            var lightObject = new GameObject(FallbackLightName);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }
    }

    private static GameObject CreateFallbackLocalAvatar()
    {
        var avatar = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        avatar.name = "Local Player Avatar";
        avatar.transform.position = new Vector3(5f, 1f, 5f);
        var avatarRenderer = avatar.GetComponent<Renderer>();
        if (avatarRenderer != null)
        {
            UnityMmoMaterialFactory.Apply(avatarRenderer, new Color(0.15f, 0.49f, 0.78f));
        }

        return avatar;
    }

    public static void RefreshFallbackWorldPresentation()
    {
        if (!HasLoadedZonePresentationScene())
        {
            return;
        }

        var fallbackFloor = GameObject.Find(FallbackFloorName);
        if (fallbackFloor != null)
        {
            Object.Destroy(fallbackFloor);
        }

        var fallbackLight = GameObject.Find(FallbackLightName);
        if (fallbackLight != null)
        {
            Object.Destroy(fallbackLight);
        }
    }

    private static bool HasLoadedZonePresentationScene()
    {
        for (var i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                continue;
            }

            if (scene.name == UnityMmoSceneNames.World || scene.name == UnityMmoSceneNames.Login || scene.name == UnityMmoSceneNames.CharacterCreation || scene.name == UnityMmoSceneNames.CharacterSelection)
            {
                continue;
            }

            return true;
        }

        return false;
    }
}
}
