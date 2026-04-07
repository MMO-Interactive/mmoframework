using UnityEngine;

[System.Serializable]
public sealed class PatientVitals
{
    [Range(0f, 1f)] public float oxygenation = 0.98f;
    [Range(0f, 1f)] public float hydration = 0.85f;
    [Range(0f, 1f)] public float nutrition = 0.8f;
    [Range(0f, 1f)] public float infectionLoad = 0.05f;
    [Range(0f, 1f)] public float bloodPressure = 0.72f;
    [Range(0f, 1f)] public float bloodGlucose = 0.55f;
    [Range(0f, 1f)] public float toxinLoad = 0.04f;
    [Range(0f, 1f)] public float pain = 0.1f;
    public float heartRate = 72f;
    public float temperatureC = 37f;
}
