using UnityEngine;

public class SimulationController : MonoBehaviour
{
    public ParticleControllerGPU controller;

    public void StartSimulation()
    {
        if (controller.enabled == false)
        {
            controller.enabled = true;
        }
    }
    
    public void StopSimulation()
    {
        if (controller.enabled)
        {
            controller.enabled = false;
        }
    }
}
