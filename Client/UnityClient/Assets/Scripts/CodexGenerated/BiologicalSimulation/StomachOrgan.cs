public sealed class StomachOrgan : BiologicalOrgan
{
    public StomachOrgan() : base("Stomach") { }

    public override void Simulate(float deltaTime, PatientVitals vitals)
    {
        if (vitals.nutrition < 0.2f || vitals.infectionLoad > 0.65f) ApplyStress(deltaTime * 0.045f);
        else Recover(deltaTime * 0.025f);
    }
}
