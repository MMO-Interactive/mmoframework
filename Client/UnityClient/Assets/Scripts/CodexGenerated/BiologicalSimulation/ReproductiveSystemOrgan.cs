public sealed class ReproductiveSystemOrgan : BiologicalOrgan
{
    public ReproductiveSystemOrgan() : base("Reproductive System") { }

    public override void Simulate(float deltaTime, PatientVitals vitals)
    {
        if (vitals.infectionLoad > 0.7f || vitals.toxinLoad > 0.65f) ApplyStress(deltaTime * 0.035f);
        else Recover(deltaTime * 0.015f);
    }
}
