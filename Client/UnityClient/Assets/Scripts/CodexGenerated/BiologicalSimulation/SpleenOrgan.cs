public sealed class SpleenOrgan : BiologicalOrgan
{
    public SpleenOrgan() : base("Spleen") { }

    public override void Simulate(float deltaTime, PatientVitals vitals)
    {
        if (vitals.infectionLoad > 0.5f || vitals.bloodPressure < 0.25f) ApplyStress(deltaTime * 0.05f);
        else Recover(deltaTime * 0.02f);
    }
}
