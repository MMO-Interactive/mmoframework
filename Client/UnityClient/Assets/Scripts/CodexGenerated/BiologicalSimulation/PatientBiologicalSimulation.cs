using System.Collections.Generic;
using UnityEngine;

public sealed class PatientBiologicalSimulation : MonoBehaviour
{
    [SerializeField] private PatientVitals vitals = new PatientVitals();
    private readonly List<BiologicalOrgan> organs = new List<BiologicalOrgan>();

    public IReadOnlyList<BiologicalOrgan> Organs => organs;
    public PatientVitals Vitals => vitals;

    private void Awake()
    {
        organs.Add(new BrainOrgan());
        organs.Add(new HeartOrgan());
        organs.Add(new LungsOrgan());
        organs.Add(new LiverOrgan());
        organs.Add(new KidneysOrgan());
        organs.Add(new StomachOrgan());
        organs.Add(new IntestinesOrgan());
        organs.Add(new PancreasOrgan());
        organs.Add(new SpleenOrgan());
        organs.Add(new BladderOrgan());
        organs.Add(new SkinOrgan());
        organs.Add(new EyesOrgan());
        organs.Add(new EarsOrgan());
        organs.Add(new ThyroidOrgan());
        organs.Add(new AdrenalGlandsOrgan());
        organs.Add(new BoneMarrowOrgan());
        organs.Add(new MusclesOrgan());
        organs.Add(new ReproductiveSystemOrgan());
    }

    private void Update()
    {
        float deltaTime = Time.deltaTime;
        for (int i = 0; i < organs.Count; i++)
        {
            organs[i].Simulate(deltaTime, vitals);
        }
    }

    public float GetOverallHealth01()
    {
        if (organs.Count == 0) return 1f;
        float total = 0f;
        for (int i = 0; i < organs.Count; i++) total += organs[i].Health01;
        return total / organs.Count;
    }
}
