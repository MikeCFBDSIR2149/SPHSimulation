using UnityEngine;

public class MonoSingleton<T> : MonoBehaviour where T : MonoBehaviour
{
    private static T _instance;

    public static T Instance
    {
        get
        {
            if (!_instance)
            {
                _instance = FindFirstObjectByType<T>();

                if (!_instance)
                {
                    GameObject singletonObject = new GameObject(typeof(T).Name);
                    _instance = singletonObject.AddComponent<T>();
                }
            }
            return _instance;
        }
    }

    protected virtual void Awake()
    {
        if (!_instance)
        {
            _instance = this as T;
            DontDestroyOnLoad(gameObject);
        }
        else if (_instance != this)
        {
            if (gameObject) Destroy(gameObject);
        }
    }
}
