using UnityEngine;

public sealed class SeminalVesiclesSimulation : MonoBehaviour
{
    [SerializeField] private float secretionLevel = 1f;
    [SerializeField] private float nutrientReserve = 1f;

    public void SimulateSecretion(float demand)
    {
        float used = Mathf.Clamp01(demand);
        secretionLevel = Mathf.Clamp01(secretionLevel - used * 0.1f);
        nutrientReserve = Mathf.Clamp01(nutrientReserve - used * 0.05f);
    }

    public void Replenish(float amount)
    {
        secretionLevel = Mathf.Clamp01(secretionLevel + amount);
        nutrientReserve = Mathf.Clamp01(nutrientReserve + amount * 0.5f);
    }
}
