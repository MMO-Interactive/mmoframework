public sealed class KidneysOrgan : BiologicalOrgan
{
    public KidneysOrgan() : base("Kidneys") { }

    public override void Simulate(float deltaTime, PatientVitals vitals)
    {
        if (vitals.hydration < 0.35f || vitals.bloodPressure < 0.3f || vitals.toxinLoad > 0.5f) ApplyStress(deltaTime * 0.07f);
        else Recover(deltaTime * 0.025f);
    }
}
