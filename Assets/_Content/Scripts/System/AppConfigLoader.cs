using System;
using System.Collections.Generic;
using System.IO;
using Styly.XRRig;
using UnityEngine;

namespace STYLY.TIB_Template
{
    /// <summary>
    /// 実機ビルドで書き換え可能な外部設定ファイルを読み込み、アプリ全体の起動時設定を反映する。
    ///
    /// アプリの中心位置・向きは、デモ会場でリビルド無しに位置合わせするためのもの。
    /// STYLY XR Rig を中心位置の逆変換だけ動かすことで、コンテンツ側の座標は変えずに見かけの配置を合わせる。
    /// NetSyncManager は Start で XR Rig の位置を物理オフセットとして取り込むため、
    /// それより前に反映されるよう早い ExecutionOrder の Awake で処理する。
    /// </summary>
    [DefaultExecutionOrder(-2000)]
    public class AppConfigLoader : MonoBehaviour
    {
        private const string ConfigFileName = "AppConfig.toml";

        // アプリの中心位置（m）。Recenter した地点（起動時のHMD位置）から見た位置を指定する。未指定の軸は 0。
        private const string CenterPositionXKey = "CenterPositionX";
        private const string CenterPositionYKey = "CenterPositionY";
        private const string CenterPositionZKey = "CenterPositionZ";

        // アプリの中心の向き（Y軸回転、度）。未指定なら 0。
        private const string CenterRotationYKey = "CenterRotationY";

        private static readonly HashSet<string> KnownKeys = new()
        {
            CenterPositionXKey, CenterPositionYKey, CenterPositionZKey, CenterRotationYKey,
        };

        private static string GetConfigDirectory()
        {
#if UNITY_EDITOR
            // 本プロジェクトは Unity プロジェクトがリポジトリ直下にあるため、テンプレート（../../）より 1 段浅い。
            return Path.Combine(Application.dataPath, "../ExternalResources");
#elif UNITY_ANDROID
            // /sdcard/Android/data/<PackageName>/files。アプリ固有領域なのでストレージ権限なしで adb push できる。
            return Application.persistentDataPath;
#elif UNITY_STANDALONE_OSX
            // dataPath = <App>.app/Contents/Resources/Data。.app と同階層まで4段上がる。
            return Path.Combine(Application.dataPath, "../../../..");
#elif UNITY_STANDALONE
            // dataPath = <Build>/<Product>_Data。.exe と同階層は1段上。
            return Path.Combine(Application.dataPath, "..");
#else
            return Application.dataPath;
#endif
        }

        private void Awake()
        {
            var path = Path.GetFullPath(Path.Combine(GetConfigDirectory(), ConfigFileName));
            if (!File.Exists(path))
            {
                // 初期値で作っておき、現場ではそれを書き換えるだけで済むようにする。初期値なので反映する設定は無い。
                CreateDefaultConfig(path);
                return;
            }

            AppConfig config;
            var warnings = new List<string>();
            try
            {
                config = AppConfig.Parse(File.ReadAllText(path), warnings);
            }
            catch (Exception e)
            {
                Debug.LogError($"[AppConfigLoader] {path} の読み込みに失敗しました: {e}");
                return;
            }

            foreach (var key in config.Keys)
            {
                if (!KnownKeys.Contains(key))
                {
                    warnings.Add($"未知のキーなので無視: {key}");
                }
            }
            foreach (var warning in warnings)
            {
                Debug.LogWarning($"[AppConfigLoader] {ConfigFileName} {warning}");
            }

            Debug.Log($"[AppConfigLoader] {path} を読み込みました。");

            ApplyCenter(config);
        }

        private static void CreateDefaultConfig(string path)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, DefaultConfigText);
                Debug.Log($"[AppConfigLoader] {path} が無いため初期値で作成しました。");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AppConfigLoader] {path} が無いため作成しようとしましたが失敗しました: {e}");
            }
        }

        private const string DefaultConfigText =
@"# アプリの起動時設定。リビルド無しで現場調整するためのもの。
#
# 置き場所
#   Editor : <リポジトリ>/ExternalResources/AppConfig.toml
#   Android: /sdcard/Android/data/<PackageName>/files/AppConfig.toml
#            （アンインストールすると消えるので注意。adb install -r なら残る）
# ファイルが無ければ起動時にこの初期値で作られる。

# アプリの中心位置（m）。Recenter した地点（起動時のHMD位置・向き）から見た位置で指定する。
# X: 右が正 / Y: 上が正 / Z: 前が正。
CenterPositionX = 0.0
CenterPositionY = 0.0
CenterPositionZ = 0.0

# アプリの中心の向き（Y軸回転、度）。Recenter した向きから右回りが正。
CenterRotationY = 0.0
";

        private static void ApplyCenter(AppConfig config)
        {
            var centerPosition = new Vector3(
                config.GetFloat(CenterPositionXKey, 0),
                config.GetFloat(CenterPositionYKey, 0),
                config.GetFloat(CenterPositionZKey, 0));
            var centerRotationY = config.GetFloat(CenterRotationYKey, 0);
            if (centerPosition == Vector3.zero && centerRotationY == 0)
            {
                return;
            }

            var stylyXrRig = FindFirstObjectByType<StylyXrRig>();
            if (stylyXrRig == null)
            {
                Debug.LogWarning("[AppConfigLoader] StylyXrRig がシーン内に見つからないため中心位置を反映できませんでした。");
                return;
            }

            // 物理空間上の centerPosition / centerRotationY がワールド原点・正面に来るよう、Rig を逆変換で配置する。
            var rigRotation = Quaternion.Inverse(Quaternion.Euler(0, centerRotationY, 0));
            stylyXrRig.transform.SetPositionAndRotation(rigRotation * -centerPosition, rigRotation);

            Debug.Log($"[AppConfigLoader] 中心位置 {centerPosition} / 向き {centerRotationY}° を反映しました。");
        }
    }
}
