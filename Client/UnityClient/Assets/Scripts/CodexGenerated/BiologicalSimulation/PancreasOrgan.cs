public sealed class PancreasOrgan : BiologicalOrgan
{
    public PancreasOrgan() : base("Pancreas") { }

    public override void Simulate(float deltaTime, PatientVitals vitals)
    {
        if (vitals.bloodGlucose < 0.18f || vitals.bloodGlucose > 0.88f) ApplyStress(deltaTime * 0.06f);
        else Recover(deltaTime * 0.025f);
    }
}
