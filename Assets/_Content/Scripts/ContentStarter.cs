using UnityEngine;
using UnityEngine.Playables;
using R3;

namespace STYLY.TIB_Template
{
    public class ContentStarter : MonoBehaviour
    {
        [SerializeField] private PlayableDirector _director;
        void Start()
        {
            RpcManager.Instance.AsObservable(Rpc.ContentStart)
                .Subscribe(_ => _director.Play())
                .AddTo(this);
        }
   }
}
