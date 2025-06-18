using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UI
{
    public class UIParamController : UIBase, IInputFieldPanelCallback
    {
        public Slider slider;
        public TextMeshProUGUI valueText;
        public Button inputButton;
        public string paramName;
        public float minValue;
        public float maxValue;
        public bool isInteger;
        
        public Action<float> OnParamValueChanged;

        private void Awake()
        {
            slider.minValue = minValue;
            slider.maxValue = maxValue;
            slider.wholeNumbers = isInteger;
            slider.onValueChanged.AddListener(SetParamValue);
        }

        private void OnEnable()
        {
            inputButton.onClick.AddListener(ActivateInputFieldPanel);
        }
        
        private void OnDisable()
        {
            inputButton.onClick.RemoveListener(ActivateInputFieldPanel);
        }

        private void SetParamValue(float value)
        {
            slider.value = value;
            OnParamValueChanged?.Invoke(value);
            if (isInteger)
            {
                int realValue = Mathf.RoundToInt(value);
                valueText.text = realValue.ToString();
            }
            else
            {
                valueText.text = value.ToString("F3");
            }
        }
        
        public void SetParamValueWithoutCallback(float value)
        {
            slider.value = value;
            if (isInteger)
            {
                int realValue = Mathf.RoundToInt(value);
                valueText.text = realValue.ToString();
            }
            else
            {
                valueText.text = value.ToString("F3");
            }
        }
        
        private void ActivateInputFieldPanel()
        {
            UIInputFieldPanel inputPanel = UIManager.Instance.OpenUI("InputFieldPanel") as UIInputFieldPanel;
            if (inputPanel == null) return;
            inputPanel.Init(200, "自定义 " + paramName, $"范围 {minValue} - {maxValue} " + (isInteger ? " (整数)" : " (浮点数)"), this);
            inputPanel.inputField.text = isInteger ? Mathf.RoundToInt(slider.value).ToString() : slider.value.ToString("F3");
        }

        public bool JudgeInputValid(string input)
        {
            if (!float.TryParse(input, out float v))
                return false;
            if (isInteger && v % 1 != 0)
                return false;
            if (v < minValue || v > maxValue)
                return false;
            return true;
        }

        public void OnInputCompleted(string input)
        {
            float v = isInteger ? Mathf.Round(float.Parse(input)) : float.Parse(input);
            SetParamValue(v);
            UIManager.Instance.CloseUI("InputFieldPanel");
        }

        public void OnInputCanceled()
        {
            UIManager.Instance.CloseUI("InputFieldPanel");
        }
    }
}
