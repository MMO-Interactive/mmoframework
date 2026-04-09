using UnityEngine;

public sealed class VasDeferensSimulation : MonoBehaviour
{
    [SerializeField] private float transportProgress;
    [SerializeField] private bool ductOpen = true;

    public void SimulateTransport(float delta)
    {
        if (!ductOpen)
        {
            return;
        }

        transportProgress = Mathf.Clamp01(transportProgress + delta);
    }

    public void SetDuctOpen(bool value)
    {
        ductOpen = value;
    }
}
