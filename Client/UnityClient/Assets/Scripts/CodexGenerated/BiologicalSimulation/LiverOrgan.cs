public sealed class LiverOrgan : BiologicalOrgan
{
    public LiverOrgan() : base("Liver") { }

    public override void Simulate(float deltaTime, PatientVitals vitals)
    {
        if (vitals.toxinLoad > 0.45f || vitals.nutrition < 0.25f) ApplyStress(deltaTime * 0.065f);
        else Recover(deltaTime * 0.02f);
    }
}
