public sealed class BoneMarrowOrgan : BiologicalOrgan
{
    public BoneMarrowOrgan() : base("Bone Marrow") { }

    public override void Simulate(float deltaTime, PatientVitals vitals)
    {
        if (vitals.infectionLoad > 0.55f || vitals.nutrition < 0.3f) ApplyStress(deltaTime * 0.04f);
        else Recover(deltaTime * 0.02f);
    }
}
