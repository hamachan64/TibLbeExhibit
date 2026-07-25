using System.Threading;
using OscJack;
using R3;
using UnityEngine;

namespace STYLY.TIB_Template
{
    /// <summary>
    /// M5Stack（StickS3等、M5Stack-XR-WorkShopファームウェア）からのOSCメッセージを受信するマネージャー。
    ///
    /// ファームウェア側は "/m5/touch" にボタン押下イベントを [pressed:int, x:int, y:int] で送信する
    /// （StickS3ではボタンAの押下/解放のみを送り、x/yは固定値）。ここでは押下の立ち上がりだけを
    /// R3 の Observable として公開し、購読側（GIPokerBootstrap）でバインダーの出し入れに使う。
    ///
    /// OSC受信そのものは OscJack（jp.keijiro.osc-jack）に任せる。OscJackのコールバックは
    /// 受信スレッド上で呼ばれるため、Unity APIやR3購読側に安全に渡せるよう、
    /// Awake時点の SynchronizationContext 経由でメインスレッドへ戻してから発火する。
    /// </summary>
    public class OSCManager : MonoBehaviour
    {
        [SerializeField, Tooltip("OSC受信ポート（M5Stack側の UNITY_OSC_PORT と一致させる。実機のwifi_config.hでは9031を使用）")]
        private int receivePort = 9031;

        [SerializeField, Tooltip("ボタン押下として扱うOSCアドレス（M5Stack-XR-WorkShopのタッチ/ボタンイベント）")]
        private string buttonOscAddress = "/m5/touch";

        private readonly Subject<Unit> buttonPressed = new();
        private OscServer server;
        private SynchronizationContext mainThreadContext;

        /// <summary>M5Stackのボタンが押された（立ち上がりエッジ）ときに発火する</summary>
        public Observable<Unit> ButtonPressed => buttonPressed;

        private void OnEnable()
        {
            mainThreadContext = SynchronizationContext.Current;
            server = new OscServer(receivePort);
            server.MessageDispatcher.AddCallback(buttonOscAddress, OnOscButtonMessage);
            Debug.Log($"[OSCManager] OSC受信を開始しました (port={receivePort})");
        }

        private void OnDisable()
        {
            if (server != null)
            {
                server.MessageDispatcher.RemoveCallback(buttonOscAddress, OnOscButtonMessage);
                server.Dispose();
                server = null;
            }
        }

        private void OnDestroy()
        {
            buttonPressed.Dispose();
        }

        private void OnOscButtonMessage(string address, OscDataHandle data)
        {
            if (data.GetElementCount() == 0 || data.GetElementAsInt(0) == 0)
            {
                return;
            }

            mainThreadContext.Post(_ => buttonPressed.OnNext(Unit.Default), null);
        }
    }
}
