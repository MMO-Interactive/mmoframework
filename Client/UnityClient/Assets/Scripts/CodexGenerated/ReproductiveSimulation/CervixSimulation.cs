using UnityEngine;

public sealed class CervixSimulation : MonoBehaviour
{
    [SerializeField] private float dilation;
    [SerializeField] private float barrierStrength = 1f;

    public void SetDilation(float value)
    {
        dilation = Mathf.Clamp01(value);
        barrierStrength = Mathf.Clamp01(1f - dilation * 0.5f);
    }

    public void ReinforceBarrier(float amount)
    {
        barrierStrength = Mathf.Clamp01(barrierStrength + amount);
    }
}
