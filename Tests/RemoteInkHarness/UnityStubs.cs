// Minimal in-memory Unity API for protocol/socket tests. This does not validate Unity rendering.
using System;
using System.Collections.Generic;
using System.Text.Json;
namespace UnityEngine {
    public class Object { public static void Destroy(Object obj) {} public static void DestroyImmediate(Object obj) {} }
    public class Component : Object { public GameObject gameObject = new GameObject(false); public Transform transform => gameObject.transform; }
    public class MonoBehaviour : Component {}
    public class GameObject : Object {
        public Transform transform;
        public GameObject(string name = "") : this(false) {}
        public GameObject(bool unused) { transform = new Transform(); }
        public void SetActive(bool active) {}
        public T AddComponent<T>() where T : Component, new() { return new T { gameObject = this }; }
        public T GetComponent<T>() where T : class { return null; }
    }
    public class Transform { public Vector3 position; public void SetParent(Transform t, bool world) {} }
    public struct Vector3 {
        public float x,y,z;
        public Vector3(float x,float y,float z) { this.x=x; this.y=y; this.z=z; }
        public static Vector3 operator +(Vector3 a,Vector3 b) => new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator -(Vector3 a,Vector3 b) => new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);
        public static Vector3 operator *(Vector3 a,float b) => new Vector3(a.x*b,a.y*b,a.z*b);
    }
    public struct Color { public Color(float r,float g,float b,float a) {} }
    public struct Bounds { public Vector3 center; }
    public class Renderer : Component { public Bounds bounds; }
    public class LineRenderer : Renderer {
        private readonly List<Vector3> points = new List<Vector3>();
        public int positionCount { get => points.Count; set { while(points.Count<value) points.Add(default); if(points.Count>value) points.RemoveRange(value,points.Count-value); } }
        public bool useWorldSpace; public Material sharedMaterial; public float startWidth,endWidth; public int numCapVertices;
        public void SetPosition(int i,Vector3 v) { points[i]=v; } public Vector3 GetPosition(int i) => points[i];
    }
    public class Material : Object { public Color color; public Material(Shader shader) {} }
    public class Shader { public static Shader Find(string name) => new Shader(); }
    public static class Application { public static bool isPlaying = false; }
    public static class Mathf { public static int Max(int a,int b) => Math.Max(a,b); }
    public static class Debug { public static void Log(string text,Object context=null) { Console.WriteLine(text); } public static void LogError(string text,Object context=null) { Console.WriteLine(text); } }
    public static class JsonUtility {
        public static T FromJson<T>(string json) {
            try { return JsonSerializer.Deserialize<T>(json,new JsonSerializerOptions { IncludeFields=true }); }
            catch(JsonException ex) { throw new ArgumentException("Invalid JSON",ex); }
        }
    }
    public class RangeAttribute : Attribute { public RangeAttribute(int min,int max) {} }
    public struct Rect { public Rect(float x,float y,float width,float height) {} }
    public static class GUI { public static bool enabled; public static void Box(Rect r,string s) {} public static void Label(Rect r,string s) {} public static bool Button(Rect r,string s) => false; }
}
namespace UnityEngine.Events { public class UnityEvent { public void Invoke() {} } }
namespace UnityEngine.SceneManagement { public static class SceneManager { public static void LoadScene(string name) {} } }
namespace UnityEditor { public class MenuItem : Attribute { public MenuItem(string name) {} } }
