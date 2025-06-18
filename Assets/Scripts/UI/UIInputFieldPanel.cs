using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UI
{
    public interface IInputFieldPanelCallback
    {
        bool JudgeInputValid(string input);
        void OnInputCompleted(string input);
        void OnInputCanceled();
    }
    
    public class UIInputFieldPanel : UIBase
    {
        public Button okButton;
        public Button cancelButton;
        
        public TextMeshProUGUI titleText;
        public TextMeshProUGUI descriptionText;
        public TextMeshProUGUI unitText;
        
        public TMP_InputField inputField;
        
        public RectTransform unitRect;
        public RectTransform inputFieldRect;
        
        private IInputFieldPanelCallback callback;
        
        private Vector2 initialInputFieldPos;

        public override void Open()
        {
            okButton.onClick.AddListener(OnOKButtonClicked);
            cancelButton.onClick.AddListener(OnCancelButtonClicked);
        }
        
        public void Init(int length, string title, string description, IInputFieldPanelCallback iCallback, string unit = "")
        {
            titleText.text = title;
            descriptionText.text = description;
            SecondInit(length, iCallback, unit);
        }

        private void SecondInit(int length, IInputFieldPanelCallback iCallback, string unit = "")
        {
            callback = iCallback;
            if (unit != "")
            {
                unitText.text = unit;
                unitRect.gameObject.SetActive(true);
                int width = unit.Length * 15;
                inputFieldRect.anchoredPosition = new Vector2((float)(800 - length) / 2, -30);
                unitRect.anchoredPosition = new Vector2((float)(length - 800) / 2, -30);
                inputFieldRect.sizeDelta = new Vector2(length - width, unitRect.sizeDelta.y);
                unitRect.sizeDelta = new Vector2(width, inputFieldRect.sizeDelta.y);
            }
            else
            {
                unitRect.gameObject.SetActive(false);
                inputFieldRect.anchoredPosition = new Vector2((float)(800 - length) / 2, -30);
                inputFieldRect.sizeDelta = new Vector2(length, inputFieldRect.sizeDelta.y);
            }
            initialInputFieldPos = inputFieldRect.anchoredPosition;
        }

        private void OnOKButtonClicked()
        {
            string input = inputField.text;
            if (callback == null) return;
            if (callback.JudgeInputValid(input))
            {
                callback.OnInputCompleted(input);
            }
            else
            {
                InputInvalid();
            }
        }

        private void OnCancelButtonClicked()
        {
            callback?.OnInputCanceled();
        }

        private void InputInvalid()
        {
            inputFieldRect.DOPunchAnchorPos(new Vector2(10f, 0f), 0.2f, 20)
                .SetUpdate(true)
                .OnComplete(() =>
                {
                    inputFieldRect.anchoredPosition = initialInputFieldPos;
                });
        }
        
        public override void Close()
        {
            okButton.onClick.RemoveListener(OnOKButtonClicked);
            cancelButton.onClick.RemoveListener(OnCancelButtonClicked);
        }
    }
}
