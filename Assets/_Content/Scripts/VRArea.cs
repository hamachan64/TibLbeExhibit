using System;
using R3;
using R3.Triggers;
using Styly.XRRig;
using UnityEngine;

namespace STYLY.TIB_Template
{
    public class VRArea : MonoBehaviour
    {
        const string PlayerTag = "Player";
        private const float _switchVRDuration = 1.2f;
        private const float _switchMRDuration = .8f;

        private readonly Subject<Collider> _onPlayerEnter = new();
        private readonly Subject<Collider> _onPlayerExit = new();

        public Observable<Collider> OnPlayerEnter => _onPlayerEnter;
        public Observable<Collider> OnPlayerExit => _onPlayerExit;

        void Start()
        {
            var stylyXrRig = FindAnyObjectByType<StylyXrRig>();

            gameObject.OnTriggerEnterAsObservable()
                .Where(other => other.CompareTag(PlayerTag))
                .Subscribe(other =>
                {
                    stylyXrRig.SwitchToVR(_switchVRDuration);
                    _onPlayerEnter.OnNext(other);
                })
                .AddTo(this);

            gameObject.OnTriggerExitAsObservable()
                .Where(other => other.CompareTag(PlayerTag))
                .Subscribe(other =>
                {
                    stylyXrRig.SwitchToMR(_switchMRDuration);
                    _onPlayerExit.OnNext(other);
                })
                .AddTo(this);
        }

        void OnDestroy()
        {
            _onPlayerEnter.Dispose();
            _onPlayerExit.Dispose();
        }
    }
}
