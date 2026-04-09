public sealed class BladderOrgan : BiologicalOrgan
{
    public BladderOrgan() : base("Bladder") { }

    public override void Simulate(float deltaTime, PatientVitals vitals)
    {
        if (vitals.hydration < 0.2f || vitals.infectionLoad > 0.7f) ApplyStress(deltaTime * 0.035f);
        else Recover(deltaTime * 0.02f);
    }
}
