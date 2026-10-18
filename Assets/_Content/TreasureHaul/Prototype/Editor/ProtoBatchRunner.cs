using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;

namespace STYLY.TIB_Template.TreasureHaul.Prototype
{
    /// <summary>
    /// 試作(S-2/S-3): ParrelSync のクローンを -batchmode で起動し、TH_Proto_Net を開いて Play するための入口。
    /// クローンのエディタは通常起動だとアセット保存ブロックのダイアログで止まる（人が OK を押す必要がある）ため、
    /// ダイアログが出ないバッチモードで起動する。何も保存しない（シーンを開いて Play するだけ）。
    /// 例: Unity -batchmode -projectPath TibLbeExhibit_clone_0 -executeMethod STYLY.TIB_Template.TreasureHaul.Prototype.ProtoBatchRunner.PlayNet
    /// </summary>
    public static class ProtoBatchRunner
    {
        const string NetScene = "Assets/_Content/TreasureHaul/Prototype/Scenes/TH_Proto_Net.unity";

        public static void PlayNet()
        {
            if (!Application.isBatchMode)
            {
                Debug.LogWarning("[ProtoBatchRunner] -batchmode でないので何もしません（メインのエディタでシーンを切り替えないため）");
                return;
            }
            EditorSceneManager.OpenScene(NetScene, OpenSceneMode.Single);
            EditorApplication.EnterPlaymode();
        }
    }
}
