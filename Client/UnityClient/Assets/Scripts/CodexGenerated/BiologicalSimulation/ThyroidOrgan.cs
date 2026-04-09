public sealed class ThyroidOrgan : BiologicalOrgan
{
    public ThyroidOrgan() : base("Thyroid") { }

    public override void Simulate(float deltaTime, PatientVitals vitals)
    {
        if (vitals.temperatureC < 35.5f || vitals.temperatureC > 39f) ApplyStress(deltaTime * 0.04f);
        else Recover(deltaTime * 0.02f);
    }
}
