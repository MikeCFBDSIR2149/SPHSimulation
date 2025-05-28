using UnityEngine;

public class SimulationGlobalStatus : MonoBehaviour
{
    public static SimulationGlobalStatus Instance { get; private set; }
    public bool inSimulation = false;

    private void Awake()
    {
        if (Instance && Instance != this) Destroy(this);
        else Instance = this;
    }
}
