using UnityEngine;

public abstract class BiologicalOrgan
{
    public string OrganName { get; }
    public float Health01 { get; protected set; } = 1f;
    public float Stress01 { get; protected set; }
    public bool IsFailing => Health01 <= 0.15f;

    protected BiologicalOrgan(string organName)
    {
        OrganName = organName;
    }

    public abstract void Simulate(float deltaTime, PatientVitals vitals);

    protected void ApplyStress(float amount)
    {
        Stress01 = Mathf.Clamp01(Stress01 + amount);
        Health01 = Mathf.Clamp01(Health01 - amount * 0.25f);
    }

    protected void Recover(float amount)
    {
        Stress01 = Mathf.Clamp01(Stress01 - amount);
        Health01 = Mathf.Clamp01(Health01 + amount * 0.2f);
    }
}
