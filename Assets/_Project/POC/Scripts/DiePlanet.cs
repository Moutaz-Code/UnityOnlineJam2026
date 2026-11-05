using System;
using System.Collections.Generic;
using UnityEngine;

namespace LuckyNumber.POC
{
    /// <summary>
    /// A d20 planet. Owns the icosahedron geometry, its face numbering (opposite faces sum to 21),
    /// and a tiny custom rigid body: scalar inertia, angular velocity only, no PhysX.
    /// The face whose normal points at the sun is the result.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class DiePlanet : MonoBehaviour
    {
        public enum RollState { Idle, Rolling, Settling }

        [Header("Shape")]
        public float circumradius = 10f;
        [Tooltip("Faces are shrunk toward their centre so the dark core shows through as edge lines.")]
        [Range(0.85f, 1f)] public float faceInset = 0.965f;
        [Tooltip("TextMesh character size. Tune if the numbers look too big or small.")]
        public float numberCharacterSize = 0.3f;

        [Header("Look")]
        public Material faceMaterial;
        public Material coreMaterial;
        public Color faceColour = new Color(0.776f, 0.863f, 1f);      // #c6dcff
        public Color luckyColour = new Color(1f, 0.863f, 0.29f);      // #ffdc4a
        public Color resultColour = new Color(1f, 0.392f, 0.392f);    // #ff6464
        public Color numberColour = new Color(0.1f, 0.1f, 0.1f);

        [Header("Sun")]
        [Tooltip("World direction from the planet centre to the sun. The face that ends up pointing here wins.")]
        public Vector3 sunDirection = new Vector3(0.25f, 1f, 0.15f);

        [Header("Roll physics")]
        [Tooltip("Scalar moment of inertia. Higher = slams spin the die less.")]
        public float inertia = 400f;
        [Tooltip("Exponential angular damping per second.")]
        public float angularDamping = 0.8f;
        [Tooltip("Below this angular speed (rad/s) the die starts settling onto the nearest face.")]
        public float settleSpeed = 0.6f;
        public float minRollTime = 0.75f;
        public float settleDuration = 0.5f;
        public float maxAngularSpeed = 12f;

        [Header("Loaded dice (upgrade prototype)")]
        [Tooltip("Spring torque pulling the lucky face toward the sun while rolling. 0 = fair die.")]
        public float loadedStrength = 0f;

        public event Action<int> Settled;   // face index
        public event Action Launched;

        public RollState State { get; private set; } = RollState.Idle;
        public int LuckyFace { get; private set; } = -1;
        public int LastResultFace { get; private set; } = -1;
        public float Inradius { get; private set; }
        public Vector3 AngularVelocity => _angularVelocity;
        public Vector3 SunDirection => sunDirection.normalized;
        public int FaceCount => _normals.Length;

        Vector3[] _vertices;
        int[] _tris;               // 20 * 3, outward winding for Unity (clockwise seen from outside)
        Vector3[] _normals;        // local
        Vector3[] _centres;        // local
        int[] _numbers;
        Renderer[] _faceRenderers;
        MaterialPropertyBlock _mpb;

        Vector3 _angularVelocity;
        float _rollTimer;
        Quaternion _settleFrom, _settleTo;
        float _settleT;
        int _settleFace;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        // ---------- public queries ----------

        public int Number(int face) => _numbers[face];
        public Vector3 LocalNormal(int face) => _normals[face];
        public Vector3 WorldNormal(int face) => transform.TransformDirection(_normals[face]);
        public Vector3 LocalCentre(int face) => _centres[face];

        /// For a regular polyhedron the ray from the centre exits through the face with the largest dot(dir, n).
        public int FaceAtLocalDirection(Vector3 localDir)
        {
            int best = 0; float bestDot = float.MinValue;
            for (int i = 0; i < _normals.Length; i++)
            {
                float d = Vector3.Dot(localDir, _normals[i]);
                if (d > bestDot) { bestDot = d; best = i; }
            }
            return best;
        }

        public int FaceFacingWorld(Vector3 worldDir) =>
            FaceAtLocalDirection(transform.InverseTransformDirection(worldDir.normalized));

        public int FaceWithNumber(int number) => Array.IndexOf(_numbers, number);

        /// Blended "up" for a walker: pure face normal in the middle of a face, a smooth mix across edges.
        public Vector3 BlendedLocalUp(Vector3 localDir, float edgeBlend)
        {
            float max = float.MinValue;
            for (int i = 0; i < _normals.Length; i++) max = Mathf.Max(max, Vector3.Dot(localDir, _normals[i]));
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < _normals.Length; i++)
            {
                float w = Vector3.Dot(localDir, _normals[i]) - (max - edgeBlend);
                if (w > 0f) sum += _normals[i] * w;
            }
            return sum.sqrMagnitude > 1e-8f ? sum.normalized : localDir.normalized;
        }

        // ---------- lifecycle ----------

        void Awake()
        {
            BuildGeometry();
            BindOrBuildVisuals();
            SnapToFace(FaceFacingWorld(SunDirection));
            RefreshColours();
        }

        void Update()
        {
            float dt = Time.deltaTime;
            switch (State)
            {
                case RollState.Rolling: StepRolling(dt); break;
                case RollState.Settling: StepSettling(dt); break;
            }
        }

        // ---------- rolling ----------

        /// Instantaneous impulse at a world point. Linear motion is ignored: the planet only spins.
        public void ApplyImpulse(Vector3 worldPoint, Vector3 worldImpulse)
        {
            if (State == RollState.Settling) return;
            Vector3 r = worldPoint - transform.position;
            _angularVelocity += Vector3.Cross(r, worldImpulse) / Mathf.Max(0.001f, inertia);
            _angularVelocity = Vector3.ClampMagnitude(_angularVelocity, maxAngularSpeed);
            _rollTimer = 0f;
            LastResultFace = -1;
            if (State != RollState.Rolling)
            {
                State = RollState.Rolling;
                Launched?.Invoke();
            }
            RefreshColours();
        }

        void StepRolling(float dt)
        {
            _rollTimer += dt;

            if (loadedStrength > 0f && LuckyFace >= 0)
            {
                // torque = k * (n x sun): rotates the lucky normal toward the sun
                Vector3 tau = loadedStrength * Vector3.Cross(WorldNormal(LuckyFace), SunDirection);
                _angularVelocity += tau / inertia * dt;
            }

            _angularVelocity *= Mathf.Exp(-angularDamping * dt);

            float speed = _angularVelocity.magnitude;
            if (speed > 1e-5f)
                transform.rotation = Quaternion.AngleAxis(speed * dt * Mathf.Rad2Deg, _angularVelocity / speed) * transform.rotation;

            if (_rollTimer >= minRollTime && speed < settleSpeed)
            {
                _settleFace = FaceFacingWorld(SunDirection);
                _settleFrom = transform.rotation;
                _settleTo = Quaternion.FromToRotation(WorldNormal(_settleFace), SunDirection) * transform.rotation;
                _settleT = 0f;
                _angularVelocity = Vector3.zero;
                State = RollState.Settling;
            }
        }

        void StepSettling(float dt)
        {
            _settleT += dt / Mathf.Max(0.01f, settleDuration);
            float t = Mathf.Clamp01(_settleT);
            t = t * t * (3f - 2f * t); // smoothstep
            transform.rotation = Quaternion.Slerp(_settleFrom, _settleTo, t);
            if (_settleT >= 1f)
            {
                transform.rotation = _settleTo;
                State = RollState.Idle;
                LastResultFace = _settleFace;
                RefreshColours();
                Settled?.Invoke(_settleFace);
            }
        }

        public void SnapToFace(int face)
        {
            transform.rotation = Quaternion.FromToRotation(WorldNormal(face), SunDirection) * transform.rotation;
        }

        public void SetLucky(int face)
        {
            LuckyFace = face;
            RefreshColours();
        }

        // ---------- colours ----------

        public void RefreshColours()
        {
            if (_faceRenderers == null) return;
            _mpb ??= new MaterialPropertyBlock();
            for (int i = 0; i < _faceRenderers.Length; i++)
            {
                if (_faceRenderers[i] == null) continue;
                Color c = faceColour;
                if (i == LastResultFace && i != LuckyFace) c = resultColour;
                if (i == LuckyFace) c = luckyColour;
                _mpb.SetColor(BaseColorId, c);
                _faceRenderers[i].SetPropertyBlock(_mpb);
            }
        }

        // ---------- geometry ----------

        public void BuildGeometry()
        {
            float phi = (1f + Mathf.Sqrt(5f)) * 0.5f;
            var v = new List<Vector3>();
            foreach (float a in new[] { -1f, 1f })
            foreach (float b in new[] { -phi, phi })
            {
                v.Add(new Vector3(0, a, b));
                v.Add(new Vector3(a, b, 0));
                v.Add(new Vector3(b, 0, a));
            }
            float scale = circumradius / Mathf.Sqrt(1f + phi * phi);
            for (int i = 0; i < v.Count; i++) v[i] *= scale;
            _vertices = v.ToArray();

            float edge = 2f * scale;
            var tris = new List<int>();
            for (int i = 0; i < 12; i++)
            for (int j = i + 1; j < 12; j++)
            for (int k = j + 1; k < 12; k++)
            {
                if (!IsEdge(i, j, edge) || !IsEdge(j, k, edge) || !IsEdge(i, k, edge)) continue;
                Vector3 c = (_vertices[i] + _vertices[j] + _vertices[k]) / 3f;
                Vector3 n = Vector3.Cross(_vertices[j] - _vertices[i], _vertices[k] - _vertices[i]);
                // Unity: Cross(b-a, c-a) points to the visible side, so make it point outward.
                if (Vector3.Dot(n, c) < 0f) tris.AddRange(new[] { i, k, j });
                else tris.AddRange(new[] { i, j, k });
            }
            _tris = tris.ToArray();
            int faceCount = _tris.Length / 3;

            _normals = new Vector3[faceCount];
            _centres = new Vector3[faceCount];
            for (int f = 0; f < faceCount; f++)
            {
                Vector3 a = _vertices[_tris[f * 3]], b = _vertices[_tris[f * 3 + 1]], c = _vertices[_tris[f * 3 + 2]];
                _centres[f] = (a + b + c) / 3f;
                _normals[f] = Vector3.Cross(b - a, c - a).normalized;
            }
            Inradius = Vector3.Dot(_centres[0], _normals[0]);

            // Numbering: opposite faces sum to 21, like a real d20.
            _numbers = new int[faceCount];
            int next = 1;
            for (int f = 0; f < faceCount; f++)
            {
                if (_numbers[f] != 0) continue;
                int opp = FaceAtLocalDirection(-_normals[f]);
                _numbers[f] = next;
                _numbers[opp] = 21 - next;
                next++;
            }
        }

        bool IsEdge(int i, int j, float edge) => Mathf.Abs(Vector3.Distance(_vertices[i], _vertices[j]) - edge) < edge * 0.01f;

        const string FacesRootName = "Faces";

        void BindOrBuildVisuals()
        {
            Transform root = transform.Find(FacesRootName);
            if (root == null || root.childCount != FaceCount + 1) BuildVisuals();
            root = transform.Find(FacesRootName);
            _faceRenderers = new Renderer[FaceCount];
            for (int i = 0; i < FaceCount; i++) _faceRenderers[i] = root.GetChild(i).GetComponent<Renderer>();
        }

        /// Creates the face objects. Called by the editor builder so the die is visible in edit mode,
        /// and at runtime if the children are missing.
        public void BuildVisuals()
        {
            if (_normals == null) BuildGeometry();

            Transform old = transform.Find(FacesRootName);
            if (old != null)
            {
                if (Application.isPlaying) Destroy(old.gameObject);
                else DestroyImmediate(old.gameObject);
            }

            var root = new GameObject(FacesRootName).transform;
            root.SetParent(transform, false);

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            for (int f = 0; f < FaceCount; f++)
            {
                Vector3 a = _vertices[_tris[f * 3]], b = _vertices[_tris[f * 3 + 1]], c = _vertices[_tris[f * 3 + 2]];
                Vector3 centre = _centres[f];
                a = Vector3.Lerp(centre, a, faceInset);
                b = Vector3.Lerp(centre, b, faceInset);
                c = Vector3.Lerp(centre, c, faceInset);

                var mesh = new Mesh { name = $"D20Face_{_numbers[f]}" };
                mesh.vertices = new[] { a, b, c };
                mesh.normals = new[] { _normals[f], _normals[f], _normals[f] };
                mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 1) };
                mesh.triangles = new[] { 0, 1, 2 };
                mesh.RecalculateBounds();

                var go = new GameObject($"Face_{_numbers[f]:00}");
                go.transform.SetParent(root, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = faceMaterial;

                // Number label, readable from outside. TextMesh is cheap and WebGL-safe for a prototype.
                var label = new GameObject("Number");
                label.transform.SetParent(go.transform, false);
                Vector3 tangentUp = (_vertices[_tris[f * 3]] - centre).normalized;
                label.transform.localPosition = centre + _normals[f] * 0.02f;
                label.transform.localRotation = Quaternion.LookRotation(-_normals[f], tangentUp);
                var tm = label.AddComponent<TextMesh>();
                tm.text = _numbers[f].ToString();
                tm.font = font;
                tm.fontSize = 96;
                tm.characterSize = numberCharacterSize;
                tm.anchor = TextAnchor.MiddleCenter;
                tm.alignment = TextAlignment.Center;
                tm.fontStyle = FontStyle.Bold;
                tm.color = numberColour;
                var labelRenderer = label.GetComponent<MeshRenderer>();
                if (labelRenderer == null) labelRenderer = label.AddComponent<MeshRenderer>();
                labelRenderer.sharedMaterial = font.material;
                labelRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            // Dark core slightly inside the faces: shows through the inset gaps as edge lines.
            var core = new GameObject("Core");
            core.transform.SetParent(root, false);
            core.transform.localScale = Vector3.one * 0.995f;
            var coreMesh = new Mesh { name = "D20Core", vertices = _vertices, triangles = _tris };
            coreMesh.RecalculateNormals();
            coreMesh.RecalculateBounds();
            core.AddComponent<MeshFilter>().sharedMesh = coreMesh;
            core.AddComponent<MeshRenderer>().sharedMaterial = coreMaterial;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(transform.position, transform.position + sunDirection.normalized * circumradius * 2f);
        }
    }
}
