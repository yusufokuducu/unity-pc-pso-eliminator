using System;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace UnityEngine
{
    public enum HideFlags
    {
        None = 0,
        HideAndDontSave = 61
    }

    public class Object
    {
        public string name { get; set; } = string.Empty;
        public HideFlags hideFlags { get; set; }
        public static void DestroyImmediate(Object obj) { }
        public static T FindFirstObjectByType<T>() where T : Object => null;
    }

    public class ScriptableObject : Object
    {
        public static T CreateInstance<T>() where T : ScriptableObject, new() => new T();
    }

    public class Component : Object
    {
        public GameObject gameObject { get; set; }
    }

    public class MonoBehaviour : Component { }

    public sealed class GameObject : Object
    {
        public GameObject() { }
        public GameObject(string name) { this.name = name; }
        public T AddComponent<T>() where T : Component, new() => new T { gameObject = this };
    }

    public sealed class Shader : Object
    {
        public bool isSupported { get; set; } = true;
    }

    public sealed class Material : Object
    {
        public Shader shader { get; set; }
        public string[] shaderKeywords { get; set; } = Array.Empty<string>();
        public int passCount { get; set; } = 1;
    }

    public sealed class ShaderVariantCollection : Object
    {
        public struct ShaderVariant
        {
            public ShaderVariant(Shader shader, PassType passType, params string[] keywords) { }
        }

        public int variantCount { get; set; }
        public int warmedUpVariantCount { get; set; }
        public bool isWarmedUp => warmedUpVariantCount >= variantCount;
        public void Clear() { variantCount = 0; warmedUpVariantCount = 0; }
        public bool Add(ShaderVariant variant) { variantCount++; return true; }
        public bool WarmUpProgressively(int count) { warmedUpVariantCount = Math.Min(variantCount, warmedUpVariantCount + count); return isWarmedUp; }
    }

    public sealed class RenderTexture : Object
    {
        public int antiAliasing { get; set; } = 1;
        public RenderTexture(int width, int height, int depth, GraphicsFormat format) { }
        public bool Create() => true;
        public void Release() { }
    }

    public struct BoneWeight
    {
        public int boneIndex0, boneIndex1, boneIndex2, boneIndex3;
        public float weight0, weight1, weight2, weight3;
    }

    public sealed class Mesh : Object
    {
        public Vector3[] vertices { get; set; }
        public Vector3[] normals { get; set; }
        public Vector4[] tangents { get; set; }
        public Vector2[] uv { get; set; }
        public Vector2[] uv2 { get; set; }
        public Color[] colors { get; set; }
        public BoneWeight[] boneWeights { get; set; }
        public Matrix4x4[] bindposes { get; set; }
        public int[] triangles { get; set; }
    }

    public static class Graphics
    {
        public static void ExecuteCommandBuffer(CommandBuffer buffer) { }
    }

    public static class SystemInfo
    {
        public static GraphicsDeviceType graphicsDeviceType => GraphicsDeviceType.Direct3D12;
        public static string graphicsDeviceName => "Stub DX12 GPU";
        public static string graphicsDeviceVersion => "1.0.0";
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class DisallowMultipleComponent : Attribute { }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class AddComponentMenu : Attribute
    {
        public AddComponentMenu(string menuName) { }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class CreateAssetMenuAttribute : Attribute
    {
        public string fileName { get; set; }
        public string menuName { get; set; }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class HeaderAttribute : Attribute
    {
        public HeaderAttribute(string header) { }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class RangeAttribute : Attribute
    {
        public RangeAttribute(float min, float max) { }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SerializeField : Attribute { }

    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => new Vector2(0, 0);
        public static Vector2 up => new Vector2(0, 1);
        public static Vector2 right => new Vector2(1, 0);
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => new Vector3(0, 0, 0);
        public static Vector3 up => new Vector3(0, 1, 0);
        public static Vector3 right => new Vector3(1, 0, 0);
        public static Vector3 back => new Vector3(0, 0, -1);
        public static Vector3 operator *(Vector3 v, float s) => new Vector3(v.x * s, v.y * s, v.z * s);
    }

    public struct Vector4
    {
        public float x, y, z, w;
        public Vector4(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
    }

    public struct Matrix4x4
    {
        public static Matrix4x4 identity => new Matrix4x4();
    }

    public struct Color
    {
        public static Color black => new Color();
    }

    public static class Mathf
    {
        public static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
        public static float Min(float a, float b) => a < b ? a : b;
        public static int Min(int a, int b) => a < b ? a : b;
        public static float Max(float a, float b) => a > b ? a : b;
        public static int Max(int a, int b) => a > b ? a : b;
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
    }

    public static class Time
    {
        public static int frameCount { get; set; }
        public static float unscaledDeltaTime { get; set; } = 0.0083f;
    }

    public static class Debug
    {
        public static void Log(object message) { }
    }

    public sealed class GUIStyle { }

    public sealed class GUILayoutOption { }

    public static class GUILayout
    {
        public static bool Button(string text, params GUILayoutOption[] options) => false;
        public static GUILayoutOption Height(float h) => new GUILayoutOption();
    }
}

namespace UnityEngine.Experimental.Rendering
{
    public enum GraphicsFormat
    {
        None = 0,
        R8G8B8A8_UNorm = 8,
        R16G16B16A16_SFloat = 48,
        B10G11R11_UFloatPack32 = 59
    }
}

namespace UnityEngine.Rendering
{
    public enum GraphicsDeviceType
    {
        Direct3D11 = 2,
        Direct3D12 = 18,
        Vulkan = 21
    }

    public enum PassType
    {
        Normal = 0,
        ForwardBase = 4,
        ForwardAdd = 5,
        ShadowCaster = 8,
        Meta = 11,
        ScriptableRenderPipeline = 13,
        DepthOnly = 15
    }

    public sealed class CommandBuffer : IDisposable
    {
        public string name { get; set; }
        public void Clear() { }
        public void SetRenderTarget(UnityEngine.RenderTexture rt) { }
        public void ClearRenderTarget(bool clearDepth, bool clearColor, UnityEngine.Color backgroundColor) { }
        public void DrawMesh(UnityEngine.Mesh mesh, UnityEngine.Matrix4x4 matrix, UnityEngine.Material material, int submeshIndex, int shaderPass) { }
        public void Dispose() { }
    }
}

namespace UnityEditor
{
    public enum MessageType { None, Info, Warning, Error }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class MenuItem : Attribute
    {
        public MenuItem(string itemName) { }
    }

    public class EditorWindow : UnityEngine.ScriptableObject
    {
        public UnityEngine.Vector2 minSize { get; set; }
        public static T GetWindow<T>(string title) where T : EditorWindow, new() => new T();
        public void Show() { }
    }

    public static class EditorStyles
    {
        public static UnityEngine.GUIStyle boldLabel { get; } = new UnityEngine.GUIStyle();
        public static UnityEngine.GUIStyle miniLabel { get; } = new UnityEngine.GUIStyle();
        public static UnityEngine.GUIStyle helpBox { get; } = new UnityEngine.GUIStyle();
    }

    public static class EditorGUILayout
    {
        public static void Space(float width = 6f) { }
        public static void LabelField(string label, params object[] args) { }
        public static void HelpBox(string message, MessageType type) { }
        public static UnityEngine.Object ObjectField(string label, UnityEngine.Object obj, Type objType, bool allowSceneObjects) => obj;
        public static void BeginHorizontal() { }
        public static void EndHorizontal() { }
        public static void BeginVertical(params object[] args) { }
        public static void EndVertical() { }
        public static UnityEngine.Vector2 BeginScrollView(UnityEngine.Vector2 pos) => pos;
        public static void EndScrollView() { }
    }

    public static class EditorUtility
    {
        public static void SetDirty(UnityEngine.Object target) { }
        public static string SaveFilePanel(string title, string directory, string defaultName, string extension) => string.Empty;
        public static void RevealInFinder(string path) { }
    }

    public static class Selection
    {
        public static UnityEngine.Object activeObject { get; set; }
        public static UnityEngine.GameObject activeGameObject { get; set; }
    }

    public static class Undo
    {
        public static void RegisterCreatedObjectUndo(UnityEngine.Object obj, string name) { }
    }

    public static class AssetDatabase
    {
        public static string[] FindAssets(string filter) => Array.Empty<string>();
        public static string GUIDToAssetPath(string guid) => string.Empty;
        public static T LoadAssetAtPath<T>(string assetPath) where T : UnityEngine.Object => null;
        public static string GetAssetPath(UnityEngine.Object assetObject) => string.Empty;
        public static void CreateAsset(UnityEngine.Object asset, string path) { }
        public static void SaveAssets() { }
    }
}
