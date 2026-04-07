public sealed class HeartOrgan : BiologicalOrgan
{
    public HeartOrgan() : base("Heart") { }

    public override void Simulate(float deltaTime, PatientVitals vitals)
    {
        if (vitals.bloodPressure < 0.35f || vitals.bloodPressure > 0.95f || vitals.oxygenation < 0.8f) ApplyStress(deltaTime * 0.08f);
        else Recover(deltaTime * 0.025f);
    }
}
