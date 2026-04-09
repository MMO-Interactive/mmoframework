using UnityEngine;

public sealed class UterusSimulation : MonoBehaviour
{
    [SerializeField] private float liningThickness = 0.5f;
    [SerializeField] private float contractionStrength;

    public void SimulateCycle(float cycleNormalized)
    {
        float phase = Mathf.Clamp01(cycleNormalized);
        liningThickness = Mathf.Clamp01(Mathf.Sin(phase * Mathf.PI) * 0.8f + 0.1f);
    }

    public void SetContractionStrength(float value)
    {
        contractionStrength = Mathf.Clamp01(value);
    }
}
