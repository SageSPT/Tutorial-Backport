using HarmonyLib;
using UnityEngine;

namespace TutorialBackport.Client;

internal static class AreaLightFields
{
    public static readonly AccessTools.FieldRef<AreaLight, Vector3> Position = AccessTools.FieldRefAccess<AreaLight, Vector3>("vector3_3");
    public static readonly AccessTools.FieldRef<AreaLight, Vector3> Forward = AccessTools.FieldRefAccess<AreaLight, Vector3>("vector3_4");
    public static readonly AccessTools.FieldRef<AreaLight, float> AtanFov = AccessTools.FieldRefAccess<AreaLight, float>("_atanFov");
    public static readonly AccessTools.FieldRef<AreaLight, Matrix4x4> LightVerts = AccessTools.FieldRefAccess<AreaLight, Matrix4x4>("matrix4x4_0");
    public static readonly AccessTools.FieldRef<AreaLight, Matrix4x4> ObjectToWorld = AccessTools.FieldRefAccess<AreaLight, Matrix4x4>("matrix4x4_1");
    public static readonly AccessTools.FieldRef<AreaLight, Matrix4x4> ClipCube = AccessTools.FieldRefAccess<AreaLight, Matrix4x4>("matrix4x4_2");
    public static readonly AccessTools.FieldRef<AreaLight, Matrix4x4> Projection = AccessTools.FieldRefAccess<AreaLight, Matrix4x4>("matrix4x4_3");
    public static readonly AccessTools.FieldRef<AreaLight, bool> HasShadowCube = AccessTools.FieldRefAccess<AreaLight, bool>("bool_1");
    public static readonly AccessTools.FieldRef<AreaLight, bool> HasInvertedShadowCube = AccessTools.FieldRefAccess<AreaLight, bool>("bool_2");
    public static readonly AccessTools.FieldRef<AreaLight, Vector4[]> ShadowPlanes = AccessTools.FieldRefAccess<AreaLight, Vector4[]>("_shadowCubePlanes");
    public static readonly AccessTools.FieldRef<AreaLight, Vector4[]> InvertedShadowPlanes = AccessTools.FieldRefAccess<AreaLight, Vector4[]>("_invertedShadowCubePlanes");
    public static readonly AccessTools.FieldRef<AreaLight, MeshRenderer> SourceRenderer = AccessTools.FieldRefAccess<AreaLight, MeshRenderer>("m_SourceRenderer");
    public static readonly AccessTools.FieldRef<AreaLight, MaterialPropertyBlock> Props = AccessTools.FieldRefAccess<AreaLight, MaterialPropertyBlock>("m_props");
}
