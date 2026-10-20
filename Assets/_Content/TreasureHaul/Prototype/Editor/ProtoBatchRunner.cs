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

        const string FoundationScene = "Assets/_Content/TreasureHaul/Prototype/Scenes/TH_Foundation_Client.unity";

        /// <summary>#37 の結合確認: SessionClient と NetSync だけを置いた確認用シーンを Play する（端末役）。</summary>
        public static void PlayFoundationClient()
        {
            if (!Application.isBatchMode)
            {
                Debug.LogWarning("[ProtoBatchRunner] -batchmode でないので何もしません（メインのエディタでシーンを切り替えないため）");
                return;
            }
            EditorSceneManager.OpenScene(FoundationScene, OpenSceneMode.Single);
            EditorApplication.EnterPlaymode();
        }

        const string OperatorScene = "Assets/_Content/TreasureHaul/Scenes/TreasureHaulOperatorClient.unity";

        /// <summary>#38 の結合確認: 運営PC（ホスト）役。時間をメモリ上で短縮し、端末が来たら自動で開始する。何も保存しない。</summary>
        public static void PlayGrabHost()
        {
            if (!Application.isBatchMode)
            {
                Debug.LogWarning("[ProtoBatchRunner] -batchmode でないので何もしません（メインのエディタでシーンを切り替えないため）");
                return;
            }
            EditorSceneManager.OpenScene(OperatorScene, OpenSceneMode.Single);
            new GameObject("GrabHostDriver").AddComponent<GrabHostDriver>();
            EditorApplication.EnterPlaymode();
        }

        /// <summary>#42 の結合確認: 運営PC（ホスト）役。保留・本編開始・回収・結果・中断を決まった時刻で操作する。何も保存しない。</summary>
        public static void PlayOpeningHost()
        {
            if (!Application.isBatchMode)
            {
                Debug.LogWarning("[ProtoBatchRunner] -batchmode でないので何もしません（メインのエディタでシーンを切り替えないため）");
                return;
            }
            EditorSceneManager.OpenScene(OperatorScene, OpenSceneMode.Single);
            new GameObject("OpeningHostDriver").AddComponent<OpeningHostDriver>();
            EditorApplication.EnterPlaymode();
        }

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
