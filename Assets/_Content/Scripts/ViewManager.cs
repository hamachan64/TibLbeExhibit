using R3;
using UnityEngine;
using UnityEngine.InputSystem;

namespace STYLY.TIB_Template
{
    public class ViewManager : MonoBehaviour
    {
        [SerializeField] private GameObject toggleVisibleTarget;
        [SerializeField] private InputActionReference toggleAction;
        [SerializeField] private VRArea vrArea;
        [SerializeField] private GameObject informationObj;

        void OnEnable()
        {
            toggleVisibleTarget.SetActive(false);
            toggleAction.action.Enable();
            toggleAction.action.performed += OnToggle;
            
            vrArea.OnPlayerEnter
                .Subscribe(_ => informationObj.SetActive(false))
                .AddTo(this);
            vrArea.OnPlayerExit
                .Subscribe(_ => informationObj.SetActive(true))
                .AddTo(this);
        }

        void OnDisable()
        {
            toggleAction.action.performed -= OnToggle;
            toggleAction.action.Disable();
        }

        private void OnToggle(InputAction.CallbackContext context)
        {
            toggleVisibleTarget.SetActive(!toggleVisibleTarget.activeSelf);
        }
    }
}