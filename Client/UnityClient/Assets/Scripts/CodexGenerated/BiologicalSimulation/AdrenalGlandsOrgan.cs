public sealed class AdrenalGlandsOrgan : BiologicalOrgan
{
    public AdrenalGlandsOrgan() : base("Adrenal Glands") { }

    public override void Simulate(float deltaTime, PatientVitals vitals)
    {
        if (vitals.pain > 0.65f || vitals.bloodPressure < 0.3f) ApplyStress(deltaTime * 0.045f);
        else Recover(deltaTime * 0.02f);
    }
}
