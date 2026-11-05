using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

// Only scene ownership, cloning, transforms and input state are simulated.
// These fixtures cannot validate Unity rendering or measure the startup stall.
namespace UnityEngine
{
    public class Object
    {
        public string name;
        public bool Destroyed;
        public static bool operator ==(Object a, Object b)
            => ReferenceEquals(a, b) || ((ReferenceEquals(a, null) || a.Destroyed) && (ReferenceEquals(b, null) || b.Destroyed));
        public static bool operator !=(Object a, Object b) => !(a == b);
        public override bool Equals(object value) => ReferenceEquals(this, value);
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
        public static void Destroy(Object value)
        {
            if (value == null) return;
            value.Destroyed = true;
            if (value is GameObject go)
            {
                foreach (Component component in go.Components.ToArray())
                {
                    component.GetType().GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(component, null);
                    component.Destroyed = true;
                }
                foreach (Transform child in go.transform.Children.ToArray()) Destroy(child.gameObject);
                go.transform.SetParent(null, false);
            }
        }
        public static GameObject Instantiate(GameObject source, Transform parent, bool worldPositionStays)
        {
            var map = new Dictionary<Object, Object>();
            GameObject Copy(GameObject original, Transform owner)
            {
                var result = new GameObject(original.name);
                map[original] = result; map[original.transform] = result.transform;
                result.SetActive(original.activeSelf);
                result.transform.SetParent(owner, false);
                result.transform.localPosition = original.transform.localPosition;
                result.transform.localScale = original.transform.localScale;
                result.transform.localRotation = original.transform.localRotation;
                foreach (Component component in original.Components.Where(c => !(c is Transform)))
                    map[component] = result.AddComponent(component.GetType());
                foreach (Transform child in original.transform.Children) Copy(child.gameObject, result.transform);
                return result;
            }
            GameObject clone = Copy(source, parent);
            foreach (var pair in map)
                if (pair.Key is Component old && !(old is Transform))
                    foreach (FieldInfo field in old.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                    {
                        if (field.IsInitOnly || field.DeclaringType == typeof(Object)) continue;
                        object value = field.GetValue(old);
                        field.SetValue(pair.Value, value is Object obj && map.TryGetValue(obj, out Object mapped) ? mapped : value);
                    }
            return clone;
        }
    }
    public class Component : Object
    {
        public GameObject gameObject { get; internal set; }
        public new string name { get => gameObject?.name; set { if (gameObject != null) gameObject.name = value; } }
        public Transform transform => gameObject.transform;
        public T GetComponent<T>() where T : Component => gameObject.GetComponent<T>();
        public T[] GetComponentsInChildren<T>(bool includeInactive = false) where T : Component
            => gameObject.GetComponentsInChildren<T>(includeInactive);
    }
    public class Behaviour : Component { public bool enabled = true; }
    public class MonoBehaviour : Behaviour { }
    public class GameObject : Object
    {
        internal readonly List<Component> Components = new List<Component>();
        public readonly Transform transform;
        public bool activeSelf = true;
        public int layer;
        public bool activeInHierarchy => activeSelf && (transform.parent == null || transform.parent.gameObject.activeInHierarchy);
        public GameObject(string name) { this.name = name; transform = new Transform { gameObject = this }; Components.Add(transform); }
        public void SetActive(bool value) { activeSelf = value; }
        public T AddComponent<T>() where T : Component, new() => (T)AddComponent(typeof(T));
        public Component AddComponent(Type type)
        { var component = (Component)Activator.CreateInstance(type, true); component.gameObject = this; Components.Add(component); return component; }
        public T GetComponent<T>() where T : Component => Components.OfType<T>().FirstOrDefault(c => !c.Destroyed);
        public T[] GetComponentsInChildren<T>(bool includeInactive = false) where T : Component
        {
            var list = new List<T>();
            if (includeInactive || activeInHierarchy) list.AddRange(Components.OfType<T>().Where(c => !c.Destroyed));
            foreach (Transform child in transform.Children) list.AddRange(child.gameObject.GetComponentsInChildren<T>(includeInactive));
            return list.ToArray();
        }
    }
    public sealed class Transform : Component
    {
        public readonly List<Transform> Children = new List<Transform>();
        public Transform parent;
        public int childCount => Children.Count;
        public Transform GetChild(int index) => Children[index];
        public Vector3 localPosition, localScale = Vector3.one;
        public Quaternion localRotation;
        public Vector3 position
        {
            get => parent == null ? localPosition : parent.position + localPosition * parent.lossyScale;
            set => localPosition = parent == null ? value : (value - parent.position) / parent.lossyScale;
        }
        public Vector3 lossyScale => parent == null ? localScale : localScale * parent.lossyScale;
        public Quaternion rotation { get => localRotation; set => localRotation = value; }
        public Vector3 right => Vector3.right;
        public void SetParent(Transform target, bool worldPositionStays)
        {
            Vector3 p = position, s = lossyScale;
            parent?.Children.Remove(this); parent = target; parent?.Children.Add(this);
            if (worldPositionStays) { position = p; localScale = target == null ? s : s / target.lossyScale; }
        }
        public bool IsChildOf(Transform owner) => this == owner || (parent != null && parent.IsChildOf(owner));
        public Vector3 TransformPoint(Vector3 point) => position + point * lossyScale;
        public Transform Find(string value) => Children.FirstOrDefault(c => c.gameObject.name == value);
    }
    public struct Quaternion
    {
        public static Quaternion identity => default;
        public static Quaternion operator *(Quaternion a, Quaternion b) => default;
    }
    public struct Vector3
    {
        public float x, y, z;
        public float sqrMagnitude => x*x + y*y + z*z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 one => new Vector3(1, 1, 1);
        public static Vector3 zero => new Vector3(0, 0, 0);
        public static Vector3 right => new Vector3(1, 0, 0);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator *(Vector3 a, Vector3 b) => new Vector3(a.x * b.x, a.y * b.y, a.z * b.z);
        public static Vector3 operator /(Vector3 a, Vector3 b) => new Vector3(a.x / b.x, a.y / b.y, a.z / b.z);
        public static Vector3 operator *(Vector3 a, float b) => new Vector3(a.x * b, a.y * b, a.z * b);
    }
    public struct Bounds
    {
        public Vector3 size, center;
        public Vector3 min => center - size * .5f;
        public Vector3 max => center + size * .5f;
    }
    public class Collider : Component { public Vector3 Size = new Vector3(2, 1, 1); public Bounds bounds => new Bounds { center = transform.position, size = Size * transform.lossyScale }; }
    public class MaterialPropertyBlock
    {
        public readonly Dictionary<string, float> Values = new Dictionary<string, float>();
        public void SetFloat(string key, float value) { Values[key] = value; }
        public float GetFloat(string key) => Values.TryGetValue(key, out float value) ? value : 0f;
    }
    public class Renderer : Behaviour
    {
        public readonly MaterialPropertyBlock Properties = new MaterialPropertyBlock();
        public void GetPropertyBlock(MaterialPropertyBlock block)
        { block.Values.Clear(); foreach (var pair in Properties.Values) block.Values[pair.Key] = pair.Value; }
        public void SetPropertyBlock(MaterialPropertyBlock block)
        { Properties.Values.Clear(); foreach (var pair in block.Values) Properties.Values[pair.Key] = pair.Value; }
    }
    public class MeshRenderer : Renderer { }
    public class Animator : Behaviour { }
    public class Font : Object { }
    public struct Color { public float r,g,b,a; }
    public struct Vector2 { public float x,y; }
    public struct Rect
    {
        public float xMin,yMin,xMax,yMax;
        public float width => xMax-xMin;
        public float height => yMax-yMin;
        public Vector2 center => new Vector2 { x=(xMin+xMax)/2, y=(yMin+yMax)/2 };
        public static Rect MinMaxRect(float x1,float y1,float x2,float y2) => new Rect { xMin=x1, yMin=y1, xMax=x2, yMax=y2 };
    }
    public sealed class Camera : Component
    {
        public Rect pixelRect = Rect.MinMaxRect(0,0,1920,1080);
        public int cullingMask = -1;
        public Vector3 WorldToScreenPoint(Vector3 p) => new Vector3(p.x*100+960,p.y*100+540,p.z+10);
        public Vector3 ScreenToWorldPoint(Vector3 p) => new Vector3((p.x-960)/100,(p.y-540)/100,p.z-10);
    }
}
public static class CameraUtils
{
    public static UnityEngine.Camera Camera = new UnityEngine.GameObject("Native UI Camera").AddComponent<UnityEngine.Camera>();
    public static UnityEngine.Camera FindProjectionCameraForObject(UnityEngine.GameObject go) => Camera;
}
