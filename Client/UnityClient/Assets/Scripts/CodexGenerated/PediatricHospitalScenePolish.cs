using UnityEngine;

public sealed class PediatricHospitalScenePolish : MonoBehaviour
{
    [SerializeField] private string sceneObjective = "Stabilize pediatric ward flow";

    private void Start()
    {
        Debug.Log($"Hospital scene polish active: {sceneObjective}");
    }
}
