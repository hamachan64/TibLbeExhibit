using Styly.NetSync;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

namespace STYLY.TIB_Template
{
    /// <summary>
    /// 名前指定でシーンを追加ロードする。
    ///
    /// UserClient と MasterClient で同じシーンを共有したい場合に、別途Environmentのような共通シーンを分けて管理することが多い。
    /// その際に UserClient, MasterClient それぞれの起動シーンから共通シーンを追加ロードするため、このクラスを用意した。
    /// </summary>
    public class AdditiveSceneLoader : MonoBehaviour
    {
        [SerializeField] private string _sceneName = "Environment";

        [SerializeField]
        [Tooltip("追加ロードしたシーンのLight Settingsを有効にする（SetActiveScene）")]
        private bool _applyLightSettings = false;

        async void Start()
        {
            // 開発中は予めマルチシーンロード完了状態で起動することが多いので、ロード済みかチェックする
            bool alreadyLoaded = false;
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var sceneName = SceneManager.GetSceneAt(i).name;
                if (sceneName == _sceneName)
                {
                    alreadyLoaded = true;
                    break;
                }
            }
            if ( ! alreadyLoaded)
            {
                await SceneManager.LoadSceneAsync(_sceneName, LoadSceneMode.Additive);
            }

            if (_applyLightSettings)
            {
                var loadedScene = SceneManager.GetSceneByName(_sceneName);
                if (loadedScene.isLoaded)
                {
                    SceneManager.SetActiveScene(loadedScene);
                }
            }
        }
    }
}
