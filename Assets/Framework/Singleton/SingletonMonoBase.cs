using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SingletonMonoBase<T> : MonoBehaviour where T : MonoBehaviour
{
    private static T instance;
    public static T Instance => instance;
    protected virtual void Awake()
    {
        if (instance != null) 
        {
            Destroy(gameObject);
            return;
        }
        instance = this as T;
        DontDestroyOnLoad(gameObject);
    }
}
