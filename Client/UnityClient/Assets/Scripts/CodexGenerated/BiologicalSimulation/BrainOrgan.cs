public sealed class BrainOrgan : BiologicalOrgan
{
    public BrainOrgan() : base("Brain") { }

    public override void Simulate(float deltaTime, PatientVitals vitals)
    {
        if (vitals.oxygenation < 0.82f || vitals.bloodGlucose < 0.25f) ApplyStress(deltaTime * 0.09f);
        else Recover(deltaTime * 0.025f);
    }
}
