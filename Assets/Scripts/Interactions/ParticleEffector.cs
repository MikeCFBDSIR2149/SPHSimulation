using System.Runtime.InteropServices;
using UnityEngine;

namespace Interactions
{
    
    [StructLayout(LayoutKind.Sequential)]
    public struct ParticleEffectorInfo
    {
        public Matrix4x4 localToWorldMatrix;
        public Matrix4x4 worldToLocalMatrix;
        public Vector3 linearVelocity;
        public Vector3 angularVelocity;
        public Vector3 center;
        public Vector3 size;
        public float stiffness;              // 排斥力强度
        public float viscosity;              // 物体边界的粘滞系数
    }
    
    [RequireComponent(typeof(Rigidbody), typeof(Collider))]
    public class ParticleEffector : MonoBehaviour
    {
        [Header("Interaction Settings")]
        public float stiffness = 50000.0f; 
        public float boundaryViscosity = 0.05f;

        private Rigidbody rb;
        private Collider col;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            col = GetComponent<Collider>();
        }

        private void Start()
        {
            SimulationGlobalStatus.Instance.RegisterParticleEffector(this);
        }
        
        public ParticleEffectorInfo GetEffectorInfo()
        {
            Bounds bounds = col.bounds;
            return new ParticleEffectorInfo
            {
                localToWorldMatrix = transform.localToWorldMatrix,
                worldToLocalMatrix = transform.worldToLocalMatrix,
                linearVelocity = rb.linearVelocity,
                angularVelocity = rb.angularVelocity,
                center = bounds.center,
                size = bounds.size,
                stiffness = this.stiffness,
                viscosity = this.boundaryViscosity
            };
        }

        public void ApplyForces(Vector3 totalForce, Vector3 totalTorque)
        {
            // Debug.Log("Forces applied to ParticleEffector: " + totalForce + ", Torque: " + totalTorque);
            rb.AddForce(totalForce, ForceMode.Force);
            rb.AddTorque(totalTorque, ForceMode.Force);
        }

        private void OnCollisionEnter(Collision other)
        {
            if (other.gameObject.layer == LayerMask.NameToLayer("Bottom"))
            {
                rb.linearVelocity = Vector3.zero;
            }
        }

        private void OnDestroy()
        {
            if (SimulationGlobalStatus.Instance && SimulationGlobalStatus.Instance.ParticleEffectors.Contains(this))
            {
                SimulationGlobalStatus.Instance.ParticleEffectors.Remove(this);
            }
        }
    }
}