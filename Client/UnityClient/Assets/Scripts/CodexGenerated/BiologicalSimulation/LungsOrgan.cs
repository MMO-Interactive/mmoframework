public sealed class LungsOrgan : BiologicalOrgan
{
    public LungsOrgan() : base("Lungs") { }

    public override void Simulate(float deltaTime, PatientVitals vitals)
    {
        if (vitals.oxygenation < 0.88f || vitals.infectionLoad > 0.55f) ApplyStress(deltaTime * 0.075f);
        else Recover(deltaTime * 0.03f);
    }
}
