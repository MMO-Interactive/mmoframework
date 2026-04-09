public sealed class EyesOrgan : BiologicalOrgan
{
    public EyesOrgan() : base("Eyes") { }

    public override void Simulate(float deltaTime, PatientVitals vitals)
    {
        if (vitals.oxygenation < 0.78f || vitals.bloodGlucose < 0.2f) ApplyStress(deltaTime * 0.035f);
        else Recover(deltaTime * 0.02f);
    }
}
