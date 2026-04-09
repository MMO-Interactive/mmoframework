public sealed class SkinOrgan : BiologicalOrgan
{
    public SkinOrgan() : base("Skin") { }

    public override void Simulate(float deltaTime, PatientVitals vitals)
    {
        if (vitals.hydration < 0.25f || vitals.temperatureC > 39.5f || vitals.infectionLoad > 0.6f) ApplyStress(deltaTime * 0.04f);
        else Recover(deltaTime * 0.025f);
    }
}
