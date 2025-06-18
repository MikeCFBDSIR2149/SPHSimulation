using System;
using UnityEngine;

public class SimulationGlobalStatus : MonoBehaviour
{
    public static SimulationGlobalStatus Instance { get; private set; }
    public bool InSimulation { get; private set; }
    
    public event Action<bool> OnStatusChanged;

    private void Awake()
    {
        if (Instance && Instance != this) Destroy(this);
        else Instance = this;
    }
    
    public void ChangeSimulationStatus(bool inSimulation)
    {
        InSimulation = inSimulation;
        OnStatusChanged?.Invoke(InSimulation);
    }
}
