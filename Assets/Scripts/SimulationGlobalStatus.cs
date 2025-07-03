using Interactions;
using System;
using System.Collections.Generic;
using UnityEngine;

public class SimulationGlobalStatus : MonoBehaviour
{
    public static SimulationGlobalStatus Instance { get; private set; }
    public bool InSimulation { get; private set; }
    
    public event Action<bool> OnStatusChanged;

    private SimulationController simulationController;
    public List<ParticleEffector> ParticleEffectors = new List<ParticleEffector>();

    private void Awake()
    {
        if (Instance && Instance != this) Destroy(this);
        else Instance = this;
    }

    public SimulationController GetSimulationController()
    {
        if (!simulationController) simulationController = FindFirstObjectByType<SimulationController>();
        return simulationController;
    }
    
    public void RegisterParticleEffector(ParticleEffector effector)
    {
        if (!ParticleEffectors.Contains(effector))
        {
            ParticleEffectors.Add(effector);
        }
    }
    
    public void ChangeSimulationStatus(bool inSimulation)
    {
        InSimulation = inSimulation;
        OnStatusChanged?.Invoke(InSimulation);
    }
}
