using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace ChillWithYou.EnvSync.Core
{
    internal sealed class TimeTransitionFlares
    {
        internal static readonly Type RenderPass = typeof(Beautify.Universal.BeautifyRendererFeature)
            .GetNestedType("BeautifyRenderPass", BindingFlags.NonPublic);
        private static readonly FieldInfo Mips = AccessTools.Field(RenderPass, "rtAF");
        private static readonly FieldInfo Descriptor = AccessTools.Field(RenderPass, "sourceDescHP");
        private static readonly PropertyInfo Mesh = AccessTools.Property(RenderPass, "fullscreenMesh");
        private static readonly Type Mip = Mips.FieldType.GetElementType();
        private static readonly FieldInfo Width = AccessTools.Field(Mip, "width");
        private static readonly FieldInfo Height = AccessTools.Field(Mip, "height");
        private static readonly FieldInfo Down = AccessTools.Field(Mip, "rtDown");
        private static readonly int BloomTexture = Shader.PropertyToID("_BloomTex");
        private static readonly int MainTexture = Shader.PropertyToID("_MainTex");
        private static readonly int Bloom = Shader.PropertyToID("_Bloom");
        private static readonly int FadedDetail = Shader.PropertyToID("_EnvSyncFadedFlareDetail");
        private readonly MaterialPropertyBlock _properties = new MaterialPropertyBlock();

        internal float DetailWeight = 1;

        internal bool Draw(CommandBuffer cmd, RenderTargetIdentifier source,
            RenderTargetIdentifier destination, Material material)
        {
            if (DetailWeight >= 1 || material.shader.name != "Hidden/Kronnect/Beautify") return false;

            // Beautify pass 11 scales its low-frequency flare, but adds _BloomTex
            // without intensity. Fade that residual separately when a preset disables
            // flares; otherwise its > 0 render branch drops a visible layer at the endpoint.
            // Native pass 4 is sample(_BloomTex) * _Bloom.x, with overwrite blending.
            // Weight 1 retains the original endpoint, not an intensity-squared effect.
            object mip = ((Array)Mips.GetValue(null)).GetValue(1);
            var descriptor = (RenderTextureDescriptor)Descriptor.GetValue(null);
            descriptor.width = (int)Width.GetValue(mip);
            descriptor.height = (int)Height.GetValue(mip);
            int originalDetail = (int)Down.GetValue(mip);
            var mesh = (Mesh)Mesh.GetValue(null, null);
            _properties.SetVector(Bloom, Vector4.one * DetailWeight);

            cmd.GetTemporaryRT(FadedDetail, descriptor, FilterMode.Bilinear);
            cmd.SetRenderTarget(new RenderTargetIdentifier(FadedDetail, 0, CubemapFace.Unknown, -1));
            cmd.SetGlobalTexture(BloomTexture, originalDetail);
            cmd.DrawMesh(mesh, Matrix4x4.identity, material, 0, 4, _properties);
            cmd.SetRenderTarget(new RenderTargetIdentifier(destination, 0, CubemapFace.Unknown, -1));
            cmd.SetGlobalTexture(MainTexture, source);
            cmd.SetGlobalTexture(BloomTexture, FadedDetail);
            cmd.DrawMesh(mesh, Matrix4x4.identity, material, 0, 11);
            cmd.SetGlobalTexture(BloomTexture, originalDetail);
            cmd.ReleaseTemporaryRT(FadedDetail);
            return true;
        }
    }
}
