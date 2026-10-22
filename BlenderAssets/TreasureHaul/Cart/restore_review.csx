using UnityEditor;
using UnityEditor.SceneManagement;
var path=SessionState.GetString("THCart_PreviousPlayStart", "");
var current=EditorSceneManager.playModeStartScene;
if(current!=null && AssetDatabase.GetAssetPath(current)=="Assets/_Content/TreasureHaul/Prototype/CartReview/CartValueReview.unity")
    EditorSceneManager.playModeStartScene=string.IsNullOrEmpty(path)?null:AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
SessionState.EraseString("THCart_PreviousPlayStart");
return "Review start override cleared/restored";
