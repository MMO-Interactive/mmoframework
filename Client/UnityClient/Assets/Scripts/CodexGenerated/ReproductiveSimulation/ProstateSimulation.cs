using UnityEngine;

public sealed class ProstateSimulation : MonoBehaviour
{
    [SerializeField] private float fluidOutput = 1f;
    [SerializeField] private float inflammationLevel;

    public void SimulateCycle(float intensity)
    {
        fluidOutput = Mathf.Clamp01(fluidOutput + intensity * 0.02f - inflammationLevel * 0.01f);
    }

    public void SetInflammation(float value)
    {
        inflammationLevel = Mathf.Clamp01(value);
    }
}
