using TMPro;
using UI;
using UnityEngine;
using UnityEngine.Rendering;

public enum SimulationType
{
    Undefined,
    GPU,
    SSF
}

public class SimulationController : MonoBehaviour
{
    public ParticleControllerGPU controllerGPU;
    public ParticleControllerSSF controllerSSF;
    public RenderPipelineAsset GPU_RPAsset;
    public RenderPipelineAsset SSF_RPAsset;
    
    public TextMeshProUGUI simulationTypeText;
    public TextMeshProUGUI undefinedWarningText;

    private SimulationType CurrentSimulationType { get; set; }

    private void Start()
    {
        CurrentSimulationType = SimulationType.Undefined;
        GraphicsSettings.defaultRenderPipeline = ReturnCurrentRPAsset();
        SetTypeText();
    }
    
    private void SetTypeText()
    {
        simulationTypeText.text = "当前模拟类型: " + CurrentSimulationType;
        undefinedWarningText.enabled = CurrentSimulationType == SimulationType.Undefined;
    }

    private MonoBehaviour ReturnCurrentController()
    {
        return CurrentSimulationType switch
        {
            SimulationType.Undefined => null,
            SimulationType.GPU => controllerGPU,
            SimulationType.SSF => controllerSSF,
            _ => (MonoBehaviour)null
        };
    }
    
    private RenderPipelineAsset ReturnCurrentRPAsset()
    {
        return CurrentSimulationType switch
        {
            SimulationType.Undefined => GPU_RPAsset,
            SimulationType.GPU => GPU_RPAsset,
            SimulationType.SSF => SSF_RPAsset,
            _ => GPU_RPAsset
        };
    }

    public void ChangeSimulationType(int simulationTypeInt)
    {
        SimulationType simulationType;
        try
        {
            simulationType = (SimulationType)simulationTypeInt;
        }
        catch
        {
            simulationType = SimulationType.Undefined;
        }
        if (SimulationGlobalStatus.Instance.InSimulation) return;
        if (CurrentSimulationType == simulationType) return;
        CurrentSimulationType = simulationType;
        GraphicsSettings.defaultRenderPipeline = ReturnCurrentRPAsset();
        SetTypeText();
        switch (CurrentSimulationType)
        {
            case SimulationType.GPU:
                UIManager.Instance.OpenUI("SimGPUCanvas");
                UIManager.Instance.CloseUI("SimSSFCanvas");
                break;
            case SimulationType.SSF:
                UIManager.Instance.OpenUI("SimSSFCanvas");
                UIManager.Instance.CloseUI("SimGPUCanvas");
                break;
            default:
                UIManager.Instance.CloseUI("SimGPUCanvas");
                UIManager.Instance.CloseUI("SimSSFCanvas");
                break;
        }
    }

    public void StartSimulation()
    {
        MonoBehaviour controller = ReturnCurrentController();
        if (!controller) return;
        SimulationGlobalStatus.Instance.ChangeSimulationStatus(true);
        if (controller.enabled == false)
        {
            controller.enabled = true;
        }
    }
    
    public void StopSimulation()
    {
        MonoBehaviour controller = ReturnCurrentController();
        if (!controller) return;
        if (controller.enabled)
        {
            controller.enabled = false;
        }
        SimulationGlobalStatus.Instance.ChangeSimulationStatus(false);
    }
}
