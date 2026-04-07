using UnityEngine;

public sealed class LeftOvarySimulation : MonoBehaviour
{
    [SerializeField] private float follicleReadiness = 1f;
    [SerializeField] private float hormoneBalance = 1f;

    public void SimulateCycleDay(float cycleNormalized)
    {
        float phase = Mathf.Clamp01(cycleNormalized);
        follicleReadiness = Mathf.Clamp01(1f - Mathf.Abs(phase - 0.5f) * 2f);
        hormoneBalance = Mathf.Clamp01(0.5f + phase * 0.5f);
    }
}
