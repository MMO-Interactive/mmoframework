public sealed class MusclesOrgan : BiologicalOrgan
{
    public MusclesOrgan() : base("Muscles") { }

    public override void Simulate(float deltaTime, PatientVitals vitals)
    {
        if (vitals.oxygenation < 0.72f || vitals.hydration < 0.25f || vitals.nutrition < 0.25f) ApplyStress(deltaTime * 0.05f);
        else Recover(deltaTime * 0.025f);
    }
}
