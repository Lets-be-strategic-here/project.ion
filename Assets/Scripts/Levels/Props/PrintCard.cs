using System.Collections.Generic;
using Ion.Presentation;
using Ion.Projection;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ion.Levels.Props
{
    using Ion.Levels.Arch;

    /// <summary>
    /// A photo print in the world: an image quad drawn with Ion/PhotoDisplay (the photo preview), or a Frost
    /// blank when there is no image ("coming soon" / not yet known), plus an optional Ion border.
    ///
    /// The image quad cannot be sliced (its UV0 holds texture coordinates, not pattern space), so every print
    /// lives under an <see cref="Interactable"/> (its own, or its pickup's): a photo captures or removes it whole
    /// by its pivot, like any device. All state is serialized, so photo copies (clones) look the same.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PrintCard : MonoBehaviour
    {
        /// <summary>Ion/PhotoDisplay's default saturation (its Polaroid look).</summary>
        public const float DefaultSaturation = 0.93f;

        [SerializeField] MeshRenderer _image;
        [SerializeField] Texture2D _texture;
        [SerializeField] GameObject _border;
        [SerializeField] float _saturation = DefaultSaturation;
        [SerializeField] Material _own;
        [System.NonSerialized] bool _madeOwn;   // false on clones: they share the source's material

        /// <summary>The photo shown, or null for a Frost blank.</summary>
        public Texture2D Image
        {
            get => _texture;
            set
            {
                _texture = value;
                Refresh();
            }
        }

        /// <summary>True while no image is known (Frost blank).</summary>
        public bool IsBlank => _texture == null;

        /// <summary>Ion border (solved prints, live exhibits). False if the card has none.</summary>
        public bool Border
        {
            get => _border != null && _border.activeSelf;
            set { if (_border != null && _border.activeSelf != value) _border.SetActive(value); }
        }

        /// <summary>0 = black and white (locked exhibit), <see cref="DefaultSaturation"/> = normal print.</summary>
        public float Saturation
        {
            get => _saturation;
            set
            {
                value = Mathf.Clamp01(value);
                if (Mathf.Approximately(value, _saturation) && _image != null && _image.sharedMaterial != null) return;
                _saturation = value;
                Refresh();
            }
        }

        /// <summary>Shows <paramref name="photo"/>'s preview (null photo = blank).</summary>
        public void Bind(PhotoData photo) => Image = photo != null ? photo.Preview : null;

        /// <summary>Shows the shot's photo now if captured, else as soon as it is.</summary>
        public void Bind(DioramaShot shot)
        {
            if (shot == null) { Image = null; return; }
            if (shot.Photo != null) { Bind(shot.Photo); return; }
            PrintCard self = this;
            shot.Captured += photo => { if (self != null) self.Bind(photo); };
        }

        internal void Init(MeshRenderer image, GameObject border)
        {
            _image = image;
            _border = border;
            Refresh();
        }

        void Refresh()
        {
            if (_image == null) return;
            if (_texture == null)
            {
                _image.sharedMaterial = Palette.Get(Mat.Frost);
                return;
            }
            if (Mathf.Abs(_saturation - DefaultSaturation) < 1e-3f)
            {
                _image.sharedMaterial = PhotoMaterials.Shared(_texture);
                return;
            }
            if (_own == null || _own.mainTexture != _texture || !_madeOwn)
            {
                if (_own != null && _madeOwn) Destroy(_own);
                _own = PhotoMaterials.Create(_texture);
                _madeOwn = _own != null;
                if (_own == null) { _image.sharedMaterial = PhotoMaterials.Shared(_texture); return; }
            }
            if (_own.HasProperty(PhotoMaterials.SaturationId)) _own.SetFloat(PhotoMaterials.SaturationId, _saturation);
            _image.sharedMaterial = _own;
        }

        void OnDestroy()
        {
            // A clone shares _own with its source; only the card that made it frees it.
            if (_own != null && _madeOwn) Destroy(_own);
        }
    }

    /// <summary>Materials for print images (Ion/PhotoDisplay outside a canvas). One shared material per texture.</summary>
    internal static class PhotoMaterials
    {
        public const string ShaderName = "Ion/PhotoDisplay";
        public const string OwnName = "PrintOwn_";
        public static readonly int SaturationId = Shader.PropertyToID("_Saturation");
        static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        static readonly Dictionary<Texture, Material> s_Shared = new Dictionary<Texture, Material>();
        static Shader s_Shader;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_Shared.Clear();
            s_Shader = null;
        }

        public static Material Shared(Texture tex)
        {
            if (tex == null) return Palette.Get(Mat.Frost);
            if (s_Shared.TryGetValue(tex, out Material m) && m != null) return m;
            m = Create(tex);
            if (m == null) return Palette.Get(Mat.Frost);
            m.name = "Print " + tex.name;
            s_Shared[tex] = m;
            return m;
        }

        /// <summary>A new print material for <paramref name="tex"/>, or null if Ion/PhotoDisplay is missing.</summary>
        public static Material Create(Texture tex)
        {
            if (s_Shader == null) s_Shader = Shader.Find(ShaderName);
            if (s_Shader == null) return null;   // shader not in the build: callers fall back to Frost
            var m = new Material(s_Shader) { name = OwnName };
            m.SetTexture(MainTexId, tex);
            // Ion/PhotoDisplay is a UI shader (ZTest [unity_GUIZTestMode]); outside a canvas force a normal depth
            // test, or prints would show through walls.
            m.SetFloat("unity_GUIZTestMode", (float)CompareFunction.LessEqual);
            return m;
        }
    }

    /// <summary>Builds print cards (image quad + optional slide mount, Ion edge and brass crop clips).</summary>
    internal static class PrintBuilder
    {
        public const float MountThickness = 0.0625f;

        /// <summary>
        /// A print centred on <paramref name="localPos"/>; its image faces the card's +Z.
        /// <paramref name="border"/> &gt; 0 adds a Paper/Sprocket slide mount (0.0625 thick, Cyanotype back) around
        /// the image; 0 gives a bare image (for frames whose mat is sliceable architecture).
        /// <paramref name="ionEdge"/> adds a toggleable Ion border (child "IonEdge").
        /// </summary>
        public static PrintCard Build(Transform parent, string name, Vector3 localPos, Quaternion localRot, Vector2 imageSize,
                                      float border, bool ionEdge, bool clips, bool interactable, bool ionEdgeOn = false)
        {
            var go = new GameObject(name);
            var t = go.transform;
            t.SetParent(parent, false);
            t.localPosition = localPos;
            t.localRotation = localRot;
            if (interactable) go.AddComponent<Interactable>();

            float hw = imageSize.x * 0.5f, hh = imageSize.y * 0.5f;
            float imageZ;
            GameObject edge = null;

            if (border > 0f)
            {
                const float th = MountThickness * 0.5f;
                Transform mount = PropBuild.Frame(t, "Mount", Vector3.zero, Quaternion.identity);
                var paper = new Surf(Mat.Paper, Pat.Sprocket);
                // Paper back behind the image ("print backs"), then the four mount borders around it.
                PropBuild.Box(mount, -hw, -hh, -th, hw, hh, 0f, Mat.Paper, PropBuild.Device);
                PropBuild.Box(mount, hw, -hh - border, -th, hw + border, hh + border, th, paper, PropBuild.Device);
                PropBuild.Box(mount, -hw - border, -hh - border, -th, -hw, hh + border, th, paper, PropBuild.Device);
                PropBuild.Box(mount, -hw, hh, -th, hw, hh + border, th, paper, PropBuild.Device);
                PropBuild.Box(mount, -hw, -hh - border, -th, hw, -hh, th, paper, PropBuild.Device);
                if (clips)
                {
                    // Brass L crop clips on the four outer corners.
                    float arm = Mathf.Min(0.125f, border * 2f), w = Mathf.Min(0.0625f, border * 0.6f);
                    float ox = hw + border, oy = hh + border, z0 = th, z1 = th + 0.015625f;
                    for (int sx = -1; sx <= 1; sx += 2)
                    for (int sy = -1; sy <= 1; sy += 2)
                    {
                        PropBuild.Box(mount, new Vector3(sx * ox, sy * oy, z0), new Vector3(sx * (ox - arm), sy * (oy - w), z1), Mat.Brass, PropBuild.Device);
                        PropBuild.Box(mount, new Vector3(sx * ox, sy * (oy - w), z0), new Vector3(sx * (ox - w), sy * (oy - arm), z1), Mat.Brass, PropBuild.Device);
                    }
                }
                PropBuild.BakeDevice(mount.gameObject);
                imageZ = 0.005f;

                if (ionEdge)
                {
                    Transform e = PropBuild.Frame(t, "IonEdge", Vector3.zero, Quaternion.identity);
                    float ex = hw + border, ey = hh + border, ew = 0.03125f, z1 = th - 0.015625f;
                    PropBuild.Box(e, ex, -ey - ew, -th, ex + ew, ey + ew, z1, Mat.Ion, PropBuild.Device);
                    PropBuild.Box(e, -ex - ew, -ey - ew, -th, -ex, ey + ew, z1, Mat.Ion, PropBuild.Device);
                    PropBuild.Box(e, -ex, ey, -th, ex, ey + ew, z1, Mat.Ion, PropBuild.Device);
                    PropBuild.Box(e, -ex, -ey - ew, -th, ex, -ey, z1, Mat.Ion, PropBuild.Device);
                    PropBuild.BakeDevice(e.gameObject);
                    edge = e.gameObject;
                }
            }
            else
            {
                imageZ = 0f;
                if (ionEdge)
                {
                    // A thin Ion line over the image's edge (bare prints sit in a recess the size of the image).
                    Transform e = PropBuild.Frame(t, "IonEdge", Vector3.zero, Quaternion.identity);
                    const float ew = 0.03125f, z0 = 0.001953125f, z1 = 0.0078125f;
                    PropBuild.Box(e, hw - ew, -hh, z0, hw, hh, z1, Mat.Ion, PropBuild.Device);
                    PropBuild.Box(e, -hw, -hh, z0, -hw + ew, hh, z1, Mat.Ion, PropBuild.Device);
                    PropBuild.Box(e, -hw + ew, hh - ew, z0, hw - ew, hh, z1, Mat.Ion, PropBuild.Device);
                    PropBuild.Box(e, -hw + ew, -hh, z0, hw - ew, -hh + ew, z1, Mat.Ion, PropBuild.Device);
                    PropBuild.BakeDevice(e.gameObject);
                    edge = e.gameObject;
                }
            }

            var img = new GameObject("Image");
            img.transform.SetParent(t, false);
            img.transform.localPosition = new Vector3(0f, 0f, imageZ);
            img.transform.localScale = new Vector3(imageSize.x, imageSize.y, 1f);
            img.AddComponent<MeshFilter>().sharedMesh = PropMeshes.ImageQuad;
            var r = img.AddComponent<MeshRenderer>();
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;

            var card = go.AddComponent<PrintCard>();
            card.Init(r, edge);
            if (edge != null) edge.SetActive(ionEdgeOn);
            return card;
        }
    }

    /// <summary>
    /// The hub light table's contact sheet: one print slot per photo of the game (art bible §7.3). Prints show
    /// their preview when known, a Frost blank when not, and an Ion border once solved. Addition to §5.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LightTableView : MonoBehaviour
    {
        [SerializeField] PrintCard[] _slots = new PrintCard[0];

        public int Count => _slots.Length;

        public PrintCard this[int index] => index >= 0 && index < _slots.Length ? _slots[index] : null;

        /// <summary>Shows <paramref name="image"/> (null = Frost blank) in slot <paramref name="index"/>, bordered Ion if solved.</summary>
        public void SetSlot(int index, Texture2D image, bool solved)
        {
            PrintCard c = this[index];
            if (c == null) return;
            c.Image = image;
            c.Border = solved;
        }

        /// <summary>Binds slot <paramref name="index"/> to a diorama shot (shown once captured).</summary>
        public void Bind(int index, DioramaShot shot)
        {
            PrintCard c = this[index];
            if (c != null) c.Bind(shot);
        }

        public void SetSolved(int index, bool solved)
        {
            PrintCard c = this[index];
            if (c != null) c.Border = solved;
        }

        internal void Init(PrintCard[] slots) => _slots = slots;
    }
}
