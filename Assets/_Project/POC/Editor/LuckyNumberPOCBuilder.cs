using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace LuckyNumber.POC.EditorTools
{
    /// <summary>
    /// Menu: Lucky Number > Build POC Scene. Creates materials and a playable scene from scratch,
    /// so nothing in the prototype depends on hand-wired prefabs. Safe to run again: it overwrites.
    /// </summary>
    public static class LuckyNumberPOCBuilder
    {
        const string Root = "Assets/_Project/POC";
        const string MatDir = Root + "/Materials";
        const string ScenePath = Root + "/LuckyNumberPOC.unity";

        [MenuItem("Lucky Number/Build POC Scene")]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            Directory.CreateDirectory(MatDir);
            AssetDatabase.Refresh();

            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (lit == null || unlit == null)
            {
                Debug.LogError("URP shaders not found. Is URP the active render pipeline?");
                return;
            }

            var faceMat = MakeMat("DieFace", lit, new Color(0.776f, 0.863f, 1f), 0.25f);
            var coreMat = MakeMat("DieCore", lit, new Color(0.19f, 0.36f, 0.67f), 0.1f);      // #305bab
            var playerMat = MakeMat("Player", lit, new Color(0.1f, 0.1f, 0.1f), 0.4f);
            var noseMat = MakeMat("PlayerNose", lit, new Color(1f, 0.39f, 0.39f), 0.4f);
            var sunMat = MakeMat("Sun", unlit, new Color(1f, 0.86f, 0.29f), 0f);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Lighting: flat ambient keeps every face readable; the sun light sells the direction.
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.42f, 0.46f, 0.58f);
            RenderSettings.skybox = null;

            // Die
            var dieGo = new GameObject("DiePlanet");
            var die = dieGo.AddComponent<DiePlanet>();
            die.faceMaterial = faceMat;
            die.coreMaterial = coreMat;
            die.BuildGeometry();
            die.BuildVisuals();

            Vector3 sunDir = die.sunDirection.normalized;

            // Sun light + visible sun
            var lightGo = new GameObject("Sun Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.6f;
            light.color = new Color(1f, 0.96f, 0.86f);
            light.shadows = LightShadows.Soft;
            lightGo.transform.rotation = Quaternion.LookRotation(-sunDir);

            var sun = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sun.name = "Sun";
            Object.DestroyImmediate(sun.GetComponent<Collider>());
            sun.transform.position = sunDir * 90f;
            sun.transform.localScale = Vector3.one * 14f;
            sun.GetComponent<MeshRenderer>().sharedMaterial = sunMat;
            sun.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

            // Player: pivot at the feet
            var player = new GameObject("Player");
            var walker = player.AddComponent<PlanetWalker>();
            walker.die = die;
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            Object.DestroyImmediate(body.GetComponent<Collider>());
            body.transform.SetParent(player.transform, false);
            body.transform.localPosition = new Vector3(0, 0.6f, 0);
            body.transform.localScale = Vector3.one * 0.6f;
            body.GetComponent<MeshRenderer>().sharedMaterial = playerMat;
            var nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
            nose.name = "Nose";
            Object.DestroyImmediate(nose.GetComponent<Collider>());
            nose.transform.SetParent(player.transform, false);
            nose.transform.localPosition = new Vector3(0, 0.85f, 0.3f);
            nose.transform.localScale = new Vector3(0.18f, 0.18f, 0.25f);
            nose.GetComponent<MeshRenderer>().sharedMaterial = noseMat;

            // Camera
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.06f, 0.08f, 0.15f);
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 500f;
            cam.fieldOfView = 55f;
            camGo.AddComponent<AudioListener>();
            var rig = camGo.AddComponent<PlanetCamera>();
            rig.target = walker;
            rig.die = die;
            walker.cameraRig = rig;
            camGo.transform.position = sunDir * 25f + Vector3.back * 10f;
            camGo.transform.LookAt(Vector3.zero);

            // Game rules
            var gameGo = new GameObject("Game");
            var game = gameGo.AddComponent<LuckyNumberGame>();
            game.die = die;
            game.walker = walker;

            EditorSceneManager.SaveScene(scene, ScenePath);

            // Put the POC first in Build Settings so WebGL test builds open straight into it.
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();

            AssetDatabase.SaveAssets();
            Debug.Log($"Lucky Number POC built at {ScenePath}. Press Play.");
        }

        static Material MakeMat(string name, Shader shader, Color colour, float smoothness)
        {
            string path = $"{MatDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", colour);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
