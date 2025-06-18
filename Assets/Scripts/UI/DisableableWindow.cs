using UnityEngine;

namespace UI
{
    public class DisableableWindow : MonoBehaviour
    {
        public GameObject disableCover;
        
        private void Start()
        {
            SimulationGlobalStatus.Instance.OnStatusChanged += TriggerCover;
            TriggerCover(SimulationGlobalStatus.Instance.InSimulation);
        }
        
        private void OnDestroy()
        {
            SimulationGlobalStatus.Instance.OnStatusChanged -= TriggerCover;
        }

        private void TriggerCover(bool isDisabled)
        {
            disableCover.SetActive(isDisabled);
        }
    }
}
