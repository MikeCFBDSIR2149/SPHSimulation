using UnityEngine;
using UnityEngine.UI;

namespace UI
{
    public class ExpandableWindow : MonoBehaviour
    {
        public GameObject foldedGroup;
        public GameObject expandedGroup;
        public Button expandButton;
        public Button foldButton;
        public bool defaultExpanded;
        public bool IsExpanded { get; private set; }

        private void Start()
        {
            IsExpanded = defaultExpanded;
            if (defaultExpanded)
            {
                Expand();
            }
            else
            {
                Fold();
            }
        }

        private void OnEnable()
        {
            expandButton.onClick.AddListener(Expand);
            foldButton.onClick.AddListener(Fold);
        }
        
        private void OnDisable()
        {
            expandButton.onClick.RemoveListener(Expand);
            foldButton.onClick.RemoveListener(Fold);
        }

        private void Expand()
        {
            foldedGroup.SetActive(false);
            expandedGroup.SetActive(true);
        }
        
        private void Fold()
        {
            foldedGroup.SetActive(true);
            expandedGroup.SetActive(false);
        }
    }
}
