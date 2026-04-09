using UnityEngine;

public sealed class RightFallopianTubeSimulation : MonoBehaviour
{
    [SerializeField] private float ciliaMotility = 1f;
    [SerializeField] private float transportProgress;

    public void SimulateTransport(float delta)
    {
        transportProgress = Mathf.Clamp01(transportProgress + Mathf.Max(0f, delta) * ciliaMotility);
    }

    public void SetMotility(float value)
    {
        ciliaMotility = Mathf.Clamp01(value);
    }
}
