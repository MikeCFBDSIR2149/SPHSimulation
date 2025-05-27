using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class SSFShadingPass : ScriptableRenderPass
{
    private readonly SSFRenderFeature.SSFSettings settings;
    private readonly Material shadingMaterial; // 用于最终着色的材质
    private readonly SSFRenderFeature feature;
    private new readonly ProfilingSampler profilingSampler = new ProfilingSampler("SSF Shading Pass");

    private RTHandle m_SmoothedDepthRT; // 需要从 SmoothPass 获取

    public SSFShadingPass(SSFRenderFeature.SSFSettings settings, Material material, SSFRenderFeature feature)
    {
        this.settings = settings;
        this.renderPassEvent = settings.renderPassEvent + 2;
        this.shadingMaterial = material;
        this.feature = feature;
    }

    // 这个 Pass 不需要分配自己的 RT，它将直接画到相机目标
    [Obsolete("This rendering path is for compatibility mode only (when Render Graph is disabled). Use Render Graph API instead.", false)]
    public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
    {
       // 如果需要中间 RT，可以在这里分配
       // 我们将直接画到相机颜色目标，所以不需要 ConfigureTarget
    }
    
    [Obsolete("This rendering path is for compatibility mode only (when Render Graph is disabled). Use Render Graph API instead.", false)]
    public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
    {
        // 尝试在这里获取 RTHandle
        if (feature?.SmoothPass != null)
        {
            m_SmoothedDepthRT = feature.SmoothPass.SmoothedDepthRT;
        }
        else
        {
            Debug.LogError("Shading Pass CONFIGURE - Feature or SmoothPass is NULL!");
            m_SmoothedDepthRT = null;
        }

        // 这个 Pass 不需要配置自己的目标，因为它会画到相机目标
        // ConfigureTarget(renderingData.cameraData.renderer.cameraColorTargetHandle);
    }

    [Obsolete("Obsolete")]
    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        RTHandle sourceRT = m_SmoothedDepthRT;
        
        if (!Application.isPlaying) return;

        if (!shadingMaterial || m_SmoothedDepthRT == null || !Application.isPlaying)
        {
            Debug.LogWarning("SSF Shading Pass: Missing material or smoothed depth RT, skipping pass.");
            return;
        }
        

        CommandBuffer cmd = CommandBufferPool.Get("SSF Shading Pass");
        RTHandle cameraColorTarget = renderingData.cameraData.renderer.cameraColorTargetHandle;

        using (new ProfilingScope(cmd, profilingSampler))
        {
            // --- 使用 Blitter, 源是 sourceRT ---
            Blitter.BlitCameraTexture(cmd, sourceRT, cameraColorTarget, shadingMaterial, 0);
        }

        context.ExecuteCommandBuffer(cmd);
        CommandBufferPool.Release(cmd);
    }

    public override void OnCameraCleanup(CommandBuffer cmd)
    {
        // 如果有临时 RT，在这里释放
    }

    // 注意: 这个 Pass 没有自己的 RT，所以不需要 Dispose
}