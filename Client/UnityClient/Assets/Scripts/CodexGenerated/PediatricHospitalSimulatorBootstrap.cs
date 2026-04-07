using UnityEngine;

public sealed class PediatricHospitalSimulatorBootstrap : MonoBehaviour
{
    [SerializeField] private int waitingPatients = 3;
    [SerializeField] private int availableBeds = 3;

    private void Start()
    {
        Debug.Log($"Pediatric Hospital Simulator online. Waiting Patients: {waitingPatients}, Available Beds: {availableBeds}");
    }
}
