public sealed class EarsOrgan : BiologicalOrgan
{
    public EarsOrgan() : base("Ears") { }

    public override void Simulate(float deltaTime, PatientVitals vitals)
    {
        if (vitals.infectionLoad > 0.75f || vitals.bloodPressure < 0.25f) ApplyStress(deltaTime * 0.03f);
        else Recover(deltaTime * 0.02f);
    }
}
