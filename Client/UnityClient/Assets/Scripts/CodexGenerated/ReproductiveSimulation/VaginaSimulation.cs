using UnityEngine;

public sealed class VaginaSimulation : MonoBehaviour
{
    [SerializeField] private float tissueHealth = 1f;
    [SerializeField] private float microbiomeBalance = 1f;

    public void ApplyStress(float amount)
    {
        float stress = Mathf.Clamp01(amount);
        tissueHealth = Mathf.Clamp01(tissueHealth - stress * 0.05f);
        microbiomeBalance = Mathf.Clamp01(microbiomeBalance - stress * 0.03f);
    }

    public void Recover(float amount)
    {
        tissueHealth = Mathf.Clamp01(tissueHealth + amount);
        microbiomeBalance = Mathf.Clamp01(microbiomeBalance + amount * 0.5f);
    }
}
