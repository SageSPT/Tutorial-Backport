using System.Runtime.InteropServices;
using UnityEngine;

namespace TutorialBackport.Client;

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct TutorialAreaLightData
{
    public Vector4 LightPos;
    public uint PackedKeywords;
    public float Pad0;
    public float ShadowPlaneFeather;
    public float InvertedShadowPlaneFeather;
    public fixed float ShadowPlanes[24];
    public fixed float InvertedShadowPlanes[24];
    public Matrix4x4 ShadowProjectionMatrix;
    public Matrix4x4 ClipCube;
    public float Hardness;
    public float AtanFov;
    public float SpecularScale;
    public float Pad1;
    public Vector4 LightDir;
    public Matrix4x4 LightVerts;

    public static TutorialAreaLightData From(AreaLight light)
    {
        light.CalculateDefaultValues();
        var d = new TutorialAreaLightData
        {
            LightPos = new Vector4(AreaLightFields.Position(light).x, AreaLightFields.Position(light).y, AreaLightFields.Position(light).z, 0f),
            LightDir = new Vector4(AreaLightFields.Forward(light).x, AreaLightFields.Forward(light).y, AreaLightFields.Forward(light).z, 0f),
            ShadowPlaneFeather = light.ShadowFeather,
            InvertedShadowPlaneFeather = light.InvertedShadowFeather,
            ShadowProjectionMatrix = AreaLightFields.Projection(light),
            ClipCube = AreaLightFields.ClipCube(light),
            Hardness = light.m_Hardness,
            AtanFov = AreaLightFields.AtanFov(light),
            SpecularScale = light.m_SpecularScale,
            LightVerts = AreaLightFields.LightVerts(light),
        };
        uint keys = 0;
        if (light.m_Ambient) keys |= 1;
        if (light.m_Negative) keys |= 2;
        if (light.m_Specular && !light.m_Negative && !light.m_Ambient) keys |= 4;
        if (light.m_Spot) keys |= 8;
        if (AreaLightFields.HasShadowCube(light)) keys |= 16;
        if (AreaLightFields.HasInvertedShadowCube(light)) keys |= 32;
        d.PackedKeywords = keys;
        if (AreaLightFields.HasShadowCube(light))
        {
            Fill(d.ShadowPlanes, AreaLightFields.ShadowPlanes(light));
        }

        if (AreaLightFields.HasInvertedShadowCube(light))
        {
            Fill(d.InvertedShadowPlanes, AreaLightFields.InvertedShadowPlanes(light));
        }

        return d;
    }

    private static void Fill(float* target, Vector4[] planes)
    {
        for (int i = 0; i < 6 && i < planes.Length; i++)
        {
            target[i * 4] = planes[i].x;
            target[i * 4 + 1] = planes[i].y;
            target[i * 4 + 2] = planes[i].z;
            target[i * 4 + 3] = planes[i].w;
        }
    }
}
