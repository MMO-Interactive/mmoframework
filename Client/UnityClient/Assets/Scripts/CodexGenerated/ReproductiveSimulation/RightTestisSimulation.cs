using UnityEngine;

public sealed class RightTestisSimulation : MonoBehaviour
{
    [SerializeField] private float hormoneOutput = 1f;
    [SerializeField] private float cellProduction = 1f;

    public void SimulateProduction(float temperatureStress)
    {
        float stress = Mathf.Clamp01(temperatureStress);
        hormoneOutput = Mathf.Clamp01(hormoneOutput - stress * 0.02f);
        cellProduction = Mathf.Clamp01(cellProduction - stress * 0.04f);
    }
}
