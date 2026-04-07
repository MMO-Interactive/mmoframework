public sealed class IntestinesOrgan : BiologicalOrgan
{
    public IntestinesOrgan() : base("Intestines") { }

    public override void Simulate(float deltaTime, PatientVitals vitals)
    {
        if (vitals.hydration < 0.3f || vitals.nutrition < 0.25f || vitals.infectionLoad > 0.6f) ApplyStress(deltaTime * 0.05f);
        else Recover(deltaTime * 0.025f);
    }
}
