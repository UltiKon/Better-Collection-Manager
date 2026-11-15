// Managed native fixtures: no Unity rendering is claimed by these checks.
namespace BetterCollectionManager
{
    // GPU artwork is validated by the game-reference build, not these managed fixtures.
    internal sealed class DeckArchiveActionIcon : System.IDisposable
    {
        internal DeckArchiveActionIcon(UnityEngine.GameObject template) { }
        internal void SetRestore(bool restore) { }
        public void Dispose() { }
    }
}
namespace UnityEngine
{
    public class Object
    {
        public static T Instantiate<T>(T original, Transform parent, bool worldPositionStays) where T : UIBButton
        {
            var copy = original.Clone(); copy.transform.parent = parent; return (T)copy;
        }
        public static void Destroy(GameObject value) { value.SetActive(false); value.Destroyed = true; }
    }
    public class GameObject
    {
        public Transform transform;
        public bool activeSelf = true, FaceVisible, Destroyed;
        public string name;
        public UIBButton Button;
        public GameObject() { transform = new Transform { gameObject = this }; }
        public void SetActive(bool value) => activeSelf = value;
    }
    public class Transform
    {
        public GameObject gameObject;
        public Transform parent;
        public string name;
        public Vector3 position, lossyScale = new(1, 1, 1);
        public bool IsChildOf(Transform ancestor) { for (Transform t = this; t != null; t = t.parent) if (t == ancestor) return true; return false; }
        public Transform Find(string path) => null;
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public float magnitude => (float)Math.Sqrt(x * x + y * y + z * z);
        public float this[int axis] { get => axis == 0 ? x : axis == 1 ? y : z; set { if (axis == 0) x = value; else if (axis == 1) y = value; else z = value; } }
    }
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
    }
    public struct Bounds { public Vector3 size; }
    public class Animation
    {
        public bool enabled = true, Stopped;
        public AnimationClip Clip = new();
        public AnimationClip GetClip(string name) => name == "NewDeck_PopUp" ? Clip : null;
        public void Stop() => Stopped = true;
    }
    public class AnimationClip
    {
        public float length = 2, SampledTime = -1;
        public void SampleAnimation(GameObject target, float time) { SampledTime = time; target.FaceVisible = target.activeSelf && time == length; }
    }
}
public class CollectionDeckTrayButton
{
    public UnityEngine.GameObject gameObject = new();
    public UnityEngine.Animation Animation = new();
    private bool m_isPoppedUp = false;
    public bool Enabled;
    public bool IsPoppedUp() => m_isPoppedUp;
    public void StopAllCoroutines() { }
    public T GetComponent<T>() where T : class => Animation as T;
    public void SetEnabled(bool enabled) => Enabled = enabled;
}
public enum UIEventType { RELEASE }
public class UberText
{
    public string Text;
    public bool GameStringLookup, RichText, ResizeToFit, ResizeToFitAndGrow;
}
public class UIBButton
{
    public UIBButton() { gameObject.Button = this; }
    public UnityEngine.GameObject gameObject = new();
    public UnityEngine.Transform transform => gameObject.transform;
    public string name, Skin = "native wide";
    public float Width = 200;
    public UberText m_ButtonText = new();
    public Action<object> Click;
    public bool Enabled;
    public void ClearEventListeners() => Click = null;
    public void AddEventListener(UIEventType type, Action<object> handler) => Click += handler;
    public void SetText(string text) => m_ButtonText.Text = text;
    public void SetEnabled(bool value, bool immediate) => Enabled = value;
    public UIBButton Clone() => new() { Skin = Skin, Width = Width };
}
public class MultiSliceElement
{
    public enum Direction { X, Y, Z }
    public Direction m_direction;
    public class Slice { public UnityEngine.GameObject m_slice; }
    public List<Slice> m_slices = new();
    public UnityEngine.Vector3 m_localSliceSpacing;
    public UnityEngine.Transform transform = new();
    public int Updates;
    public void AddSlice(UnityEngine.GameObject item) => m_slices.Add(new Slice { m_slice = item });
    public void UpdateSlices() => Updates++;
}
public class NineSliceElement
{
    public MultiSliceElement.Slice m_top = new() { m_slice = new() }, m_bottom = new() { m_slice = new() };
    public float Width = 450, Height = 200;
    public UnityEngine.Vector2 GetWorldDimensions() => new(Width, Height);
    public void SetWidth(float value) => Width = value;
}
public static class TransformUtil
{
    public static UnityEngine.Bounds GetBoundsOfChildren(UIBButton button) => new() { size = new UnityEngine.Vector3(button.Width, 10, 10) };
}
namespace BetterCollectionManager
{
    internal static class FilterButtonFeedback { internal static void RestoreNativeListeners(UIBButton button) { } }
}
