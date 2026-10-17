using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor.VersionControl;
using UnityEngine;
using UnityEngine.Events;

public class ResourcesManager : SingletonLazyBase<ResourcesManager>
{
    private abstract class ResourcesInfoBase { }
    private class ResourcesInfo<T> : ResourcesInfoBase
    {
        public T asset;
        public UnityAction<T> callback;
        public Coroutine coroutine;
        public bool isLoading;
    }
    private Dictionary<string, ResourcesInfoBase> _resourcesInfoDic = new();
    public T Load<T>(string path) where T : UnityEngine.Object
    {
        string key = path + typeof(T).Name;
        if (_resourcesInfoDic.TryGetValue(key, out var resourcesInfoBase))
        {
            if (resourcesInfoBase is ResourcesInfo<T> resourcesInfo)
            {
                if (resourcesInfo.isLoading)
                {
                    MonoManager.Instance.StopCor(resourcesInfo.coroutine);
                    T asset = Resources.Load<T>(path);
                    resourcesInfo.asset = asset;
                    resourcesInfo.callback?.Invoke(asset);
                    resourcesInfo.callback = null;
                    resourcesInfo.coroutine = null;
                    resourcesInfo.isLoading = false;
                }
                return resourcesInfo.asset;
            }
            else
                throw new Exception($"传入的类型是 {typeof(ResourcesInfo<T>).Name} ，但 dic 中 key {key} 记录的类型是 {resourcesInfoBase.GetType()} ");
        }
        else
        {
            ResourcesInfo<T> resourcesInfo = new();
            T asset = Resources.Load<T>(path);
            resourcesInfo.asset = asset;
            _resourcesInfoDic.Add(key, resourcesInfo);
            return asset;
        }

    }
    public UnityEngine.Object Load(string path, Type type)
    {
        string key = path + type.Name;
        if (_resourcesInfoDic.TryGetValue(key, out var resourcesInfoBase))
        {
            if (resourcesInfoBase is ResourcesInfo<UnityEngine.Object> resourcesInfo)
            {
                if (resourcesInfo.isLoading)
                {
                    MonoManager.Instance.StopCor(resourcesInfo.coroutine);
                    UnityEngine.Object asset = Resources.Load(path, type);
                    resourcesInfo.asset = asset;
                    resourcesInfo.callback?.Invoke(asset);
                    resourcesInfo.callback = null;
                    resourcesInfo.coroutine = null;
                    resourcesInfo.isLoading = false;
                }
                return resourcesInfo.asset;
            }
            else
                throw new Exception($"传入的类型是 {typeof(ResourcesInfo<UnityEngine.Object>).Name} ，但 dic 中 key {key} 记录的类型是 {resourcesInfoBase.GetType()} ");
        }
        else
        {
            ResourcesInfo<UnityEngine.Object> resourcesInfo = new();
            UnityEngine.Object asset = Resources.Load(path, type);
            resourcesInfo.asset = asset;
            _resourcesInfoDic.Add(key, resourcesInfo);
            return asset;
        }
    }
    public void LoadAsync<T>(string path, UnityAction<T> callback = null) where T : UnityEngine.Object
    {
        string key = path + typeof(T).Name;
        if (_resourcesInfoDic.TryGetValue(key, out var resourcesInfoBase))
        {
            if (resourcesInfoBase is ResourcesInfo<T> resourcesInfo)
            {
                if (resourcesInfo.isLoading)
                    resourcesInfo.callback += callback;
                else
                    callback?.Invoke(resourcesInfo.asset);
            }
            else
                throw new Exception($"传入的类型是 {typeof(ResourcesInfo<T>).Name} ，但 dic 中 key {key} 记录的类型是 {resourcesInfoBase.GetType()} ");

        }
        else
        {
            ResourcesInfo<T> resourcesInfo = new();
            resourcesInfo.callback += callback;
            _resourcesInfoDic.Add(key, resourcesInfo);
            resourcesInfo.coroutine = MonoManager.Instance.StartCor(LoadAsyncCoroutine<T>(path));
        }
    }
    private IEnumerator LoadAsyncCoroutine<T>(string path) where T : UnityEngine.Object
    {
        string key = path + typeof(T).Name;
        ResourcesInfo<T> resourcesInfo = _resourcesInfoDic[key] as ResourcesInfo<T>;
        resourcesInfo.isLoading = true;
        ResourceRequest request = Resources.LoadAsync<T>(path);
        yield return request;
        T asset = request.asset as T;
        resourcesInfo.asset = asset;
        resourcesInfo.isLoading = false;
        resourcesInfo.callback?.Invoke(asset);
        resourcesInfo.callback = null;
        resourcesInfo.coroutine = null;
    }
    public void LoadAsync(string path, Type type, UnityAction<UnityEngine.Object> callback = null)
    {
        string key = path + type.Name;
        if (_resourcesInfoDic.TryGetValue(key, out var resourcesInfoBase))
        {
            if (resourcesInfoBase is ResourcesInfo<UnityEngine.Object> resourcesInfo)
            {
                if (resourcesInfo.isLoading)
                    resourcesInfo.callback += callback;
                else
                    callback?.Invoke(resourcesInfo.asset);
            }
            else
                throw new Exception($"传入的类型是 {typeof(ResourcesInfo<UnityEngine.Object>).Name} ，但 dic 中 key {key} 记录的类型是 {resourcesInfoBase.GetType()} ");
        }
        else
        {
            ResourcesInfo<UnityEngine.Object> resourcesInfo = new();
            resourcesInfo.callback += callback;
            _resourcesInfoDic.Add(key, resourcesInfo);
            resourcesInfo.coroutine = MonoManager.Instance.StartCor(LoadAsyncCoroutine(path, type));
        }
    }
    private IEnumerator LoadAsyncCoroutine(string path, Type type)
    {
        string key = path + type.Name;
        ResourcesInfo<UnityEngine.Object> resourcesInfo = _resourcesInfoDic[key] as ResourcesInfo<UnityEngine.Object>;
        resourcesInfo.isLoading = true;
        ResourceRequest request = Resources.LoadAsync(path, type);
        yield return request;
        UnityEngine.Object asset = request.asset;
        resourcesInfo.asset = asset;
        resourcesInfo.isLoading = false;
        resourcesInfo.callback?.Invoke(asset);
        resourcesInfo.callback = null;
        resourcesInfo.coroutine = null;
    }
    public void UnloadAsset<T>(string path, UnityAction<T> callback = null) where T : UnityEngine.Object
    {
        string key = path + typeof(T).Name;
        if (_resourcesInfoDic.TryGetValue(key, out var resourcesInfoBase))
        {
            if (resourcesInfoBase is ResourcesInfo<T> resourcesInfo)
            {
                if (resourcesInfo.isLoading)
                    resourcesInfo.callback -= callback;
                else
                {
                    if (resourcesInfo.asset is not (GameObject or Component))
                        Resources.UnloadAsset(resourcesInfo.asset);
                    _resourcesInfoDic.Remove(key);
                }
            }
            else
                throw new Exception($"传入的类型是 {typeof(ResourcesInfo<T>).Name} ，但 dic 中 key {key} 记录的类型是 {resourcesInfoBase.GetType()} ");
        }
        else
            throw new Exception($"key {key} 在 dic 中 不存在");
    }
    public void UnloadAsset(string path, Type type, UnityAction<UnityEngine.Object> callback = null)
    {
        string key = path + type.Name;
        if (_resourcesInfoDic.TryGetValue(key, out var resourcesInfoBase))
        {
            if (resourcesInfoBase is ResourcesInfo<UnityEngine.Object> resourcesInfo)
            {
                if (resourcesInfo.isLoading)
                    resourcesInfo.callback -= callback;
                else
                {
                    if (resourcesInfo.asset is not (GameObject or Component))
                        Resources.UnloadAsset(resourcesInfo.asset);
                    _resourcesInfoDic.Remove(key);
                }
            }
            else
                throw new Exception($"传入的类型是 {typeof(ResourcesInfo<UnityEngine.Object>).Name} ，但 dic 中 key {key} 记录的类型是 {resourcesInfoBase.GetType()} ");
        }
        else
            throw new Exception($"key {key} 在 dic 中 不存在");
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
