using UnityEngine;
using System.Runtime.InteropServices;

public class ParticleTest : MonoBehaviour
{
    public Mesh mesh;
    public Material material;
    public int count = 1000;
    public Vector3 minPos = new Vector3(-5, 0, -5);
    public Vector3 maxPos = new Vector3(5, 5, 5);

    ComputeBuffer propertiesBuffer, argsBuffer;

    struct MeshProperties
    {
        public Matrix4x4 mat;
    }

    void Start()
    {
        // 初始化粒子数据
        MeshProperties[] props = new MeshProperties[count];
        for (int i = 0; i < count; i++)
        {
            Vector3 pos = new Vector3(
                Random.Range(minPos.x, maxPos.x),
                Random.Range(minPos.y, maxPos.y),
                Random.Range(minPos.z, maxPos.z));
            float scale = 0.1f;
            Matrix4x4 m = Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one * scale);
            Vector4 color = new Vector4(Random.value, Random.value, Random.value, 1);
            props[i].mat = m;
        }
        int stride = Marshal.SizeOf(typeof(MeshProperties));
        propertiesBuffer = new ComputeBuffer(count, stride, ComputeBufferType.Structured);
        propertiesBuffer.SetData(props);

        // indirect args: index count, instance count, start index, base vertex, start instance
        uint[] args = new uint[5] {
            mesh.GetIndexCount(0), (uint)count,
            mesh.GetIndexStart(0), mesh.GetBaseVertex(0), 0
        };
        argsBuffer = new ComputeBuffer(1, args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
        argsBuffer.SetData(args);
    }

    void Update()
    {
        if (propertiesBuffer == null || argsBuffer == null) return;
        material.SetBuffer("_Properties", propertiesBuffer);
        Graphics.DrawMeshInstancedIndirect(
            mesh, 0, material,
            new Bounds(Vector3.zero, Vector3.one * 100),
            argsBuffer);
    }

    void OnDestroy()
    {
        if (propertiesBuffer != null) propertiesBuffer.Release();
        if (argsBuffer != null) argsBuffer.Release();
    }
}

