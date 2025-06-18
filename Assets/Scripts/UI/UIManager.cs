using System.Collections.Generic;
using UnityEngine;

namespace UI
{
    using UIDict = Dictionary<string, UIBase>;
    
    public enum UIType
    {
        Global,
        Local
    }
    
    public class UIManager : MonoSingleton<UIManager>
    {
        private readonly UIDict GlobalUIDict = new UIDict();
        private readonly UIDict LocalUIDict = new UIDict();

        private UIDict GetUIDict(UIType type)
        {
            return type switch
            {
                UIType.Global => GlobalUIDict,
                UIType.Local => LocalUIDict,
                _ => null
            };
        }
    
        public void RegisterUI(string targetUIName, UIBase targetUIBase, UIType type = UIType.Global)
        {
            UIDict usingDict = GetUIDict(type);
            if (usingDict.ContainsKey(targetUIName)) return;
            usingDict.TryAdd(targetUIName, targetUIBase);
        }

        public void UnregisterUI(string targetUIName, UIType type)
        {
            UIDict usingDict = GetUIDict(type);
            usingDict.Remove(targetUIName);
        }

        public UIBase GetUI(string targetUIName, UIType type = UIType.Global)
        {
            UIDict usingDict = GetUIDict(type);
            usingDict.TryGetValue(targetUIName, out UIBase value);
            return value;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="type">UIType.Local only</param>
        public void ClearUI(UIType type = UIType.Local)
        {
            if (type == UIType.Global) return;
            UIDict usingDict = GetUIDict(type);
            usingDict.Clear();
        }
        
        public UIBase OpenUI(string targetUIName, UIType type = UIType.Global)
        {
            UIDict usingDict = GetUIDict(type);
            if (usingDict.TryGetValue(targetUIName, out UIBase value))
            {
                value.gameObject.SetActive(true);
                value.Open();
                return value;
            }
            
            if (type != UIType.Global) return null;
            // Only UIType.Global can load from Resources
            GameObject UI = Resources.Load<GameObject>("UI/" + targetUIName);
            if (!UI) return null;
            UI = Instantiate(UI, transform);
            RegisterUI(targetUIName, UI.GetComponent<UIBase>());
            usingDict[targetUIName].gameObject.SetActive(true);
            usingDict[targetUIName].Open();
            return usingDict[targetUIName];
        }

        public void CloseUI(string targetUIName, UIType type = UIType.Global)
        {
            UIDict usingDict = GetUIDict(type);
            if (!usingDict.TryGetValue(targetUIName, out UIBase value)) return;
            value.Close();
            value.gameObject.SetActive(false);
        }
    }
}
