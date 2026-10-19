using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using TMPro;
using System.Linq;
using System.Collections.Generic;
using STYLY.TIB_Template.TreasureHaul;

var modelPath = "Assets/_Content/TreasureHaul/Models/Cart/ExcavationCart.fbx";
var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
if (model == null) throw new System.Exception("Import model first");
var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
if (font == null) throw new System.Exception("Existing font missing");
var original = SceneManager.GetActiveScene();
var review = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
SceneManager.SetActiveScene(review);
var root = new GameObject("TH_SalvageCart");
var visual = (GameObject)PrefabUtility.InstantiatePrefab(model, review);
visual.transform.SetParent(root.transform, false);
var materials = new Dictionary<string, Material>();
foreach (var renderer in visual.GetComponentsInChildren<MeshRenderer>())
{
    var slots = renderer.sharedMaterials;
    for (int i = 0; i < slots.Length; i++)
    {
        var source = slots[i]; var key = source.name.Split('.')[0];
        if (!materials.TryGetValue(key, out var mat))
        {
            var path = "Assets/_Content/TreasureHaul/Models/Cart/Materials/" + key + ".mat";
            mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, path); }
            Color color = new Color(.12f,.16f,.14f);
            if (key.Contains("Charcoal")) color = new Color(.035f,.045f,.04f);
            if (key.Contains("Screen")) color = new Color(.008f,.012f,.032f);
            if (key.Contains("WornEnamel")) { color = Color.white; mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Content/TreasureHaul/Models/Cart/Textures/Cart_WornPaint.png")); }
            if (key.Contains("SharedSurface")) { color = Color.white; mat.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Content/TreasureHaul/Models/Cart/Textures/Stage_BaseColor.png")); }
            mat.SetColor("_BaseColor", color); mat.SetFloat("_Smoothness", .18f); mat.SetFloat("_Metallic", .25f);
            // Small baked ambient contribution keeps opaque props legible in passthrough scenes.
            mat.EnableKeyword("_EMISSION");mat.SetColor("_EmissionColor",color*.25f);
            mat.SetTexture("_EmissionMap",mat.GetTexture("_BaseMap"));
            if (key.Contains("Phosphor")) { mat.SetColor("_BaseColor",new Color(.9f,.8f,.12f));mat.EnableKeyword("_EMISSION");mat.SetColor("_EmissionColor",new Color(.9f,.8f,.12f)); }
            EditorUtility.SetDirty(mat); materials[key] = mat;
        }
        slots[i] = mat;
    }
    renderer.sharedMaterials = slots;
}
var marker = visual.GetComponentsInChildren<Transform>().Single(t => t.name.StartsWith("THCart_ValueDisplay"));
var textGo = new GameObject("CargoTotal", typeof(RectTransform));
textGo.transform.SetParent(root.transform, false);
textGo.transform.position = marker.position;
// The FBX rear normal is -Z in Unity; TMP's front also faces -Z.
textGo.transform.localRotation = Quaternion.identity;
var text = textGo.AddComponent<TextMeshPro>();
text.font = font; text.fontSharedMaterial = font.material;
text.color = new Color(1,.87f,.18f); text.fontSize = .7f;
text.enableAutoSizing = true; text.fontSizeMin = .1f; text.fontSizeMax = .7f;
text.alignment = TextAlignmentOptions.Center;
text.rectTransform.sizeDelta = new Vector2(.38f,.11f);
text.text = "0 G";
var volumeGo = new GameObject("CargoVolume"); volumeGo.transform.SetParent(root.transform,false);
var volume = volumeGo.AddComponent<BoxCollider>(); volume.isTrigger = true;
volume.center = new Vector3(0,.49f,0); volume.size = new Vector3(.64f,.34f,.88f);
var counter = root.AddComponent<CartCargoCounter>(); counter.Configure(volume,text);
var body = root.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false;
var centers = new[]{new Vector3(0,.28f,0),new Vector3(-.35f,.475f,0),new Vector3(.35f,.475f,0),new Vector3(0,.475f,-.48f),new Vector3(0,.475f,.48f)};
var sizes = new[]{new Vector3(.66f,.08f,.94f),new Vector3(.06f,.31f,1),new Vector3(.06f,.31f,1),new Vector3(.66f,.31f,.05f),new Vector3(.66f,.31f,.05f)};
for(int i=0;i<centers.Length;i++){var c=root.AddComponent<BoxCollider>();c.center=centers[i];c.size=sizes[i];}
PrefabUtility.SaveAsPrefabAsset(root,"Assets/_Content/TreasureHaul/Prefabs/SalvageCart.prefab");
var samplePositions = new[]{new Vector3(0,.32f,.19f),new Vector3(-.17f,.32f,-.20f),new Vector3(.16f,.32f,-.20f)};
var sampleNames = new[]{"amphora","cup","statuette"};
var sampleValues = new[]{1600,800,1200};
for(int i=0;i<3;i++)
{
    var sampleModel=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Content/TreasureHaul/Prototype/CartReview/"+sampleNames[i]+".fbx");
    var item=(GameObject)PrefabUtility.InstantiatePrefab(sampleModel,review);item.name="SampleCargo_"+i;
    item.transform.position=samplePositions[i];
    var renderers=item.GetComponentsInChildren<MeshRenderer>();
    float low=renderers.Min(r=>r.bounds.min.y);item.transform.position+=Vector3.up*(.32f-low);
    foreach(var renderer in renderers)
    {
        var slots=renderer.sharedMaterials;
        for(int j=0;j<slots.Length;j++)
        {
            var key=slots[j].name.Split('.')[0];
            var path="Assets/_Content/TreasureHaul/Models/Cart/Materials/Sample_"+key+".mat";
            var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(mat==null){mat=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(mat,path);}
            mat.SetColor("_BaseColor",key.Contains("Gold")?new Color(.65f,.43f,.12f):new Color(.23f,.12f,.055f));
            mat.SetFloat("_Metallic",.65f);mat.SetFloat("_Smoothness",.35f);
            mat.EnableKeyword("_EMISSION");mat.SetColor("_EmissionColor",mat.GetColor("_BaseColor")*.2f);
            if(key.ToLowerInvariant().Contains("grip")){mat.SetColor("_BaseColor",new Color(.3f,.9f,.7f));mat.EnableKeyword("_EMISSION");mat.SetColor("_EmissionColor",new Color(.3f,.9f,.7f));}
            EditorUtility.SetDirty(mat);AssetDatabase.SaveAssetIfDirty(mat);slots[j]=mat;
        }
        renderer.sharedMaterials=slots;
    }
    var valuable=item.AddComponent<CargoValuable>();valuable.SetCurrentValue(sampleValues[i]);
    var probe=new GameObject("CargoProbe");probe.transform.SetParent(item.transform,false);probe.transform.position=new Vector3(item.transform.position.x,.36f,item.transform.position.z);
    var serialized=new SerializedObject(valuable);serialized.FindProperty("cargoPoint").objectReferenceValue=probe.transform;serialized.ApplyModifiedPropertiesWithoutUndo();
    var rb=item.AddComponent<Rigidbody>();rb.useGravity=false;rb.isKinematic=true;
}
var cameraGo=new GameObject("CartReviewCamera");var camera=cameraGo.AddComponent<Camera>();
cameraGo.transform.position=new Vector3(1.5f,1.6f,-2.2f);cameraGo.transform.LookAt(new Vector3(0,.4f,0));
camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.055f,.065f,.07f);camera.nearClipPlane=.01f;
camera.fieldOfView=35;
var lightGo=new GameObject("CartReviewLight");var light=lightGo.AddComponent<Light>();light.type=LightType.Directional;light.intensity=2;
lightGo.transform.rotation=Quaternion.Euler(45,-30,0);
var fillGo=new GameObject("CartReviewFill");var fill=fillGo.AddComponent<Light>();fill.type=LightType.Directional;fill.intensity=2;
fillGo.transform.rotation=Quaternion.Euler(40,145,0);
RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;RenderSettings.ambientLight=new Color(.35f,.38f,.4f);
EditorSceneManager.SaveScene(review,"Assets/_Content/TreasureHaul/Prototype/CartReview/CartValueReview.unity");
foreach(var mat in materials.Values)AssetDatabase.SaveAssetIfDirty(mat);
SceneManager.SetActiveScene(original);EditorSceneManager.CloseScene(review,true);
return "Created SalvageCart prefab and isolated CartValueReview scene; original scenes preserved";
