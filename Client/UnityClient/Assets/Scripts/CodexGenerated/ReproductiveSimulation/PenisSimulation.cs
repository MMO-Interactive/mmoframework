using UnityEngine;

public sealed class PenisSimulation : MonoBehaviour
{
    [SerializeField] private float bloodFlow;
    [SerializeField] private float nerveResponse = 1f;

    public void SetBloodFlow(float value)
    {
        bloodFlow = Mathf.Clamp01(value);
    }

    public void SimulateResponse(float stimulus)
    {
        nerveResponse = Mathf.Clamp01(nerveResponse + Mathf.Clamp01(stimulus) * 0.05f);
    }
}
