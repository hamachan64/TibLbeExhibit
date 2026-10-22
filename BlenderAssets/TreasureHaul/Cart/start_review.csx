using UnityEditor;
using UnityEditor.SceneManagement;
var previous = EditorSceneManager.playModeStartScene;
SessionState.SetString("THCart_PreviousPlayStart",previous != null ? AssetDatabase.GetAssetPath(previous) : "");
EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/_Content/TreasureHaul/Prototype/CartReview/CartValueReview.unity");
return "Dedicated cart review selected for this PlayMode run";
