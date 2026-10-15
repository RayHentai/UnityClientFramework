using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class ResourcesManager : SingletonLazyBase<ResourcesManager>
{
    public T Load<T>(string path) where T : UnityEngine.Object
    {
        return Resources.Load<T>(path);
    }
    public UnityEngine.Object Load(string path, Type type)
    {
        return Resources.Load(path, type);
    }
    public void LoadAsync<T>(string path, UnityAction<T> callback = null) where T : UnityEngine.Object
    {
        MonoManager.Instance.StartCor(LoadAsyncCoroutine(path, callback));
    }
    private IEnumerator LoadAsyncCoroutine<T>(string path, UnityAction<T> callback) where T : UnityEngine.Object
    {
        ResourceRequest request = Resources.LoadAsync<T>(path);
        yield return request;
        callback?.Invoke(request.asset as T);
    }
    public void LoadAsync(string path, Type type, UnityAction<UnityEngine.Object> callback = null)
    {
        MonoManager.Instance.StartCor(LoadAsyncCoroutine(path, type, callback));
    }
    private IEnumerator LoadAsyncCoroutine(string path, Type type, UnityAction<UnityEngine.Object> callback)
    {
        ResourceRequest request = Resources.LoadAsync(path, type);
        yield return request;
        callback?.Invoke(request.asset);
    }
    public void UnloadAsset(UnityEngine.Object asset) 
    {
        Resources.UnloadAsset(asset);
    }
    public void UnloadUnusedAssets(UnityAction callback = null)
    {
        MonoManager.Instance.StartCor(UnloadUnusedAssetsCoroutine(callback));
    }
    private IEnumerator UnloadUnusedAssetsCoroutine(UnityAction callback = null) 
    {
        AsyncOperation operation = Resources.UnloadUnusedAssets();
        yield return operation;
        callback?.Invoke();
    }

}
