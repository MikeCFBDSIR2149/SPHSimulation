using System;
using System.Collections.Generic;
using UnityEngine;

namespace UI
{
    public class UISimGPUCanvas : UIBase
    {
        public List<UIParamController> paramControllers;
        private readonly List<Action<float>> paramUpdateActions = new List<Action<float>>();
        
        public override void Open()
        {
            base.Open();
            Init();
            AddActions();
        }

        private void Init()
        {
            paramControllers[0].SetParamValueWithoutCallback(ParticleControllerParams.Instance.particleMass);
            paramControllers[1].SetParamValueWithoutCallback(ParticleControllerParams.Instance.pressureExponentGamma);
            paramControllers[2].SetParamValueWithoutCallback(ParticleControllerParams.Instance.gravity.y);
            paramControllers[3].SetParamValueWithoutCallback(ParticleControllerParams.Instance.smoothingRadiusH);
            paramControllers[4].SetParamValueWithoutCallback(ParticleControllerParams.Instance.boundaryDamping);
            paramControllers[5].SetParamValueWithoutCallback(ParticleControllerParams.Instance.targetParticleCount);
            paramControllers[6].SetParamValueWithoutCallback(ParticleControllerParams.Instance.particleRenderScale);
            paramControllers[7].SetParamValueWithoutCallback(ParticleControllerParams.Instance.restDensityRho);
            paramControllers[8].SetParamValueWithoutCallback(ParticleControllerParams.Instance.gasConstantB);
            paramControllers[9].SetParamValueWithoutCallback(ParticleControllerParams.Instance.viscosityMu);
        }
        
        private void AddActions()
        {
            paramUpdateActions.Add(UpdateParticleMass);
            paramUpdateActions.Add(UpdatePressureExpo);
            paramUpdateActions.Add(UpdateGravity);
            paramUpdateActions.Add(UpdateSmoothingRadius);
            paramUpdateActions.Add(UpdateBoundaryDamping);
            paramUpdateActions.Add(UpdateTargetParticleCount);
            paramUpdateActions.Add(UpdateParticleRenderScale);
            paramUpdateActions.Add(UpdateRestDensity);
            paramUpdateActions.Add(UpdateGasConstant);
            paramUpdateActions.Add(UpdateViscosity);
            for (int i = 0; i < paramControllers.Count; i++)
            {
                if (i < paramUpdateActions.Count)
                {
                    paramControllers[i].OnParamValueChanged += paramUpdateActions[i];
                }
                else
                {
                    Debug.LogWarning($"No update action defined for controller {i}");
                }
            }
        }

        private void UpdateParticleMass(float value)
        {
            ParticleControllerParams.Instance.particleMass = value;
        }
        
        private void UpdatePressureExpo(float value)
        {
            ParticleControllerParams.Instance.pressureExponentGamma = value;
        }
        
        private void UpdateGravity(float value)
        {
            ParticleControllerParams.Instance.gravity = new Vector3(0, value, 0);
        }
        
        private void UpdateSmoothingRadius(float value)
        {
            ParticleControllerParams.Instance.smoothingRadiusH = value;
        }
        
        private void UpdateBoundaryDamping(float value)
        {
            ParticleControllerParams.Instance.boundaryDamping = value;
        }

        private void UpdateTargetParticleCount(float value)
        {
            ParticleControllerParams.Instance.targetParticleCount = Mathf.RoundToInt(value);
        }

        private void UpdateParticleRenderScale(float value)
        {
            ParticleControllerParams.Instance.particleRenderScale = value;
        }
        
        private void UpdateRestDensity(float value)
        {
            ParticleControllerParams.Instance.restDensityRho = value;
        }
        
        private void UpdateGasConstant(float value)
        {
            ParticleControllerParams.Instance.gasConstantB = value;
        }

        private void UpdateViscosity(float value)
        {
            ParticleControllerParams.Instance.viscosityMu = value;
        }
    }
}
