using UMA;
using UnityEngine;

namespace MMONetworking.Client
{
public static class UnityMmoUmaAvatarFactory
{
    private const string UmaAvatarPrefabPath = "MMONetworking/UMADynamicCharacterAvatar";
    private static bool _runtimeInitializationAttempted;
    private static UMAContextBase _runtimeContext;
    private static UMAGeneratorBase _runtimeGenerator;

    public static bool TryCreateAvatar(string avatarName, ulong visualSeed, out GameObject avatar)
    {
        avatar = null;
        if (!EnsureRuntime())
        {
            return false;
        }

        var avatarPrefab = Resources.Load<GameObject>(UmaAvatarPrefabPath);
        if (avatarPrefab == null)
        {
            Debug.LogWarning("UMA avatar prefab was not found at Resources/" + UmaAvatarPrefabPath + ".");
            return false;
        }

        var avatarObject = Object.Instantiate(avatarPrefab);
        avatarObject.name = avatarName;
        avatarObject.transform.position = Vector3.zero;
        avatarObject.transform.rotation = Quaternion.identity;

        var capsuleCollider = avatarObject.GetComponent<CapsuleCollider>();
        if (capsuleCollider == null)
        {
            capsuleCollider = avatarObject.AddComponent<CapsuleCollider>();
        }
        capsuleCollider.center = new Vector3(0f, 1f, 0f);
        capsuleCollider.height = 2f;
        capsuleCollider.radius = 0.4f;

        if (avatarObject.GetComponent<UMA.CharacterSystem.DynamicCharacterAvatar>() == null)
        {
            Debug.LogWarning("UMA avatar prefab did not contain DynamicCharacterAvatar.");
            Object.Destroy(avatarObject);
            return false;
        }

        NeutralizeRuntimeMotion(avatarObject);

        return true;
    }

    public static bool IsAvailable()
        => EnsureRuntime();

    private static bool EnsureRuntime()
    {
        if (_runtimeContext != null && _runtimeGenerator != null)
        {
            return true;
        }

        if (_runtimeInitializationAttempted)
        {
            return false;
        }

        _runtimeInitializationAttempted = true;

        _runtimeContext = Object.FindObjectOfType<UMAContextBase>(true);
        _runtimeGenerator = Object.FindObjectOfType<UMAGeneratorBase>(true);
        if (_runtimeContext == null)
        {
            Debug.LogWarning("UMA context was not found in the scene. Add the required UMA runtime objects before creating avatars.");
            return false;
        }

        if (_runtimeGenerator == null)
        {
            Debug.LogWarning("UMA generator was not found in the scene. Add the required UMA runtime objects before creating avatars.");
            return false;
        }

        UMAContextBase.Instance = _runtimeContext;
        var concreteContext = _runtimeContext as UMAContext;
        if (concreteContext != null)
        {
            concreteContext.Start();
            if (concreteContext.dynamicCharacterSystem != null)
            {
                concreteContext.dynamicCharacterSystem.context = concreteContext;
                concreteContext.dynamicCharacterSystem.Init();
            }
            concreteContext.ValidateDictionaries();
        }

        return true;
    }

    private static void NeutralizeRuntimeMotion(GameObject avatarObject)
    {
        if (avatarObject == null)
        {
            return;
        }

        foreach (var rigidbody in avatarObject.GetComponentsInChildren<Rigidbody>(true))
        {
            rigidbody.isKinematic = true;
            rigidbody.useGravity = false;
            rigidbody.linearVelocity = Vector3.zero;
            rigidbody.angularVelocity = Vector3.zero;
        }

        foreach (var animator in avatarObject.GetComponentsInChildren<Animator>(true))
        {
            animator.applyRootMotion = false;
        }

        foreach (var controller in avatarObject.GetComponentsInChildren<CharacterController>(true))
        {
            controller.enabled = false;
        }
    }
}
}
