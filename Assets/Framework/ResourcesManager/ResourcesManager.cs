using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class ResourcesManager : SingletonLazyBase<ResourcesManager>
{
    private abstract class ResourcesInfoBase
    {
        public int referenceCount;
    }
    private class ResourcesInfo<T> : ResourcesInfoBase
    {
        public T asset;
        public UnityAction<T> callback;
        public Coroutine coroutine;
        public bool isLoading;
        public bool isUnloadImmediate;
        public void AddReference()
        {
            referenceCount++;
        }
        public void ReleaseReference()
        {
            referenceCount--;
            if (referenceCount < 0)
                throw new Exception("引用计数 为 负数，加载/卸载资源 没有成对");
        }
    }
    private Dictionary<string, ResourcesInfoBase> _resourcesInfoDic = new();
    public T Load<T>(string path) where T : UnityEngine.Object
    {
        string key = path + "_" + typeof(T).Name;
        if (_resourcesInfoDic.TryGetValue(key, out var resourcesInfoBase))
        {
            if (resourcesInfoBase is ResourcesInfo<T> resourcesInfo)
            {
                resourcesInfo.AddReference();
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
            resourcesInfo.AddReference();
            resourcesInfo.asset = asset;
            _resourcesInfoDic.Add(key, resourcesInfo);
            return asset;
        }
    }
    public UnityEngine.Object Load(string path, Type type)
    {
        string key = path + "_" + type.Name;
        if (_resourcesInfoDic.TryGetValue(key, out var resourcesInfoBase))
        {
            if (resourcesInfoBase is ResourcesInfo<UnityEngine.Object> resourcesInfo)
            {
                resourcesInfo.AddReference();
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
            resourcesInfo.AddReference();
            resourcesInfo.asset = asset;
            _resourcesInfoDic.Add(key, resourcesInfo);
            return asset;
        }
    }
    private void UnloadIfUnreferenced<T>(string key, ResourcesInfo<T> info) where T : UnityEngine.Object
    {
        if (info.referenceCount != 0 || !info.isUnloadImmediate)
            return;
        if (info.asset is not (GameObject or Component))
            Resources.UnloadAsset(info.asset);
        _resourcesInfoDic.Remove(key);
    }
    public void LoadAsync<T>(string path, UnityAction<T> callback = null) where T : UnityEngine.Object
    {
        string key = path + "_" + typeof(T).Name;
        if (_resourcesInfoDic.TryGetValue(key, out var resourcesInfoBase))
        {
            if (resourcesInfoBase is ResourcesInfo<T> resourcesInfo)
            {
                resourcesInfo.AddReference();
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
            resourcesInfo.AddReference();
            _resourcesInfoDic.Add(key, resourcesInfo);
            resourcesInfo.coroutine = MonoManager.Instance.StartCor(LoadAsyncCoroutine<T>(path));
        }
    }
    private IEnumerator LoadAsyncCoroutine<T>(string path) where T : UnityEngine.Object
    {
        string key = path + "_" + typeof(T).Name;
        ResourcesInfo<T> resourcesInfo = _resourcesInfoDic[key] as ResourcesInfo<T>;
        resourcesInfo.isLoading = true;
        ResourceRequest request = Resources.LoadAsync<T>(path);
        yield return request;
        if (resourcesInfo.referenceCount == 0 && resourcesInfo.isUnloadImmediate)
        {
            resourcesInfo.asset = request.asset as T;
            UnloadIfUnreferenced(key, resourcesInfo);
        }
        else
        {
            T asset = request.asset as T;
            resourcesInfo.asset = asset;
            resourcesInfo.isLoading = false;
            resourcesInfo.callback?.Invoke(asset);
            resourcesInfo.callback = null;
            resourcesInfo.coroutine = null;
        }
    }
    public void LoadAsync(string path, Type type, UnityAction<UnityEngine.Object> callback = null)
    {
        string key = path + "_" + type.Name;
        if (_resourcesInfoDic.TryGetValue(key, out var resourcesInfoBase))
        {
            if (resourcesInfoBase is ResourcesInfo<UnityEngine.Object> resourcesInfo)
            {
                resourcesInfo.AddReference();
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
            resourcesInfo.AddReference();
            _resourcesInfoDic.Add(key, resourcesInfo);
            resourcesInfo.coroutine = MonoManager.Instance.StartCor(LoadAsyncCoroutine(path, type));
        }
    }
    private IEnumerator LoadAsyncCoroutine(string path, Type type)
    {
        string key = path + "_" + type.Name;
        ResourcesInfo<UnityEngine.Object> resourcesInfo = _resourcesInfoDic[key] as ResourcesInfo<UnityEngine.Object>;
        resourcesInfo.isLoading = true;
        ResourceRequest request = Resources.LoadAsync(path, type);
        yield return request;
        if (resourcesInfo.referenceCount == 0 && resourcesInfo.isUnloadImmediate)
        {
            resourcesInfo.asset = request.asset;
            UnloadIfUnreferenced(key, resourcesInfo);
        }
        else
        {
            UnityEngine.Object asset = request.asset;
            resourcesInfo.asset = asset;
            resourcesInfo.isLoading = false;
            resourcesInfo.callback?.Invoke(asset);
            resourcesInfo.callback = null;
            resourcesInfo.coroutine = null;
        }
    }
    public void UnloadAsset<T>(string path, bool isUnloadImmediately = false, UnityAction<T> callback = null) where T : UnityEngine.Object
    {
        string key = path + "_" + typeof(T).Name;
        if (_resourcesInfoDic.TryGetValue(key, out var resourcesInfoBase))
        {
            if (resourcesInfoBase is ResourcesInfo<T> resourcesInfo)
            {
                resourcesInfo.isUnloadImmediate = isUnloadImmediately;
                if (resourcesInfo.isLoading)
                {
                    resourcesInfo.ReleaseReference();
                    resourcesInfo.callback -= callback;
                }
                else
                {
                    resourcesInfo.ReleaseReference();

                    UnloadIfUnreferenced(key, resourcesInfo);
                }
            }
            else
                throw new Exception($"传入的类型是 {typeof(ResourcesInfo<T>).Name} ，但 dic 中 key {key} 记录的类型是 {resourcesInfoBase.GetType()} ");
        }
        else
            throw new Exception($"key {key} 在 dic 中 不存在");
    }
    public void UnloadAsset(string path, Type type, bool isUnloadImmediately = false, UnityAction<UnityEngine.Object> callback = null)
    {
        string key = path + "_" + type.Name;
        if (_resourcesInfoDic.TryGetValue(key, out var resourcesInfoBase))
        {
            if (resourcesInfoBase is ResourcesInfo<UnityEngine.Object> resourcesInfo)
            {
                resourcesInfo.isUnloadImmediate = isUnloadImmediately;
                if (resourcesInfo.isLoading)
                {
                    resourcesInfo.ReleaseReference();
                    resourcesInfo.callback -= callback;
                }
                else
                {
                    resourcesInfo.ReleaseReference();
                    UnloadIfUnreferenced(key, resourcesInfo);
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
        List<string> keysToRemove = new();
        foreach (var key in _resourcesInfoDic.Keys)
            if (_resourcesInfoDic[key].referenceCount == 0)
                keysToRemove.Add(key);
        foreach (var key in keysToRemove)
            _resourcesInfoDic.Remove(key);
        AsyncOperation operation = Resources.UnloadUnusedAssets();
        yield return operation;
        callback?.Invoke();
    }
    public int GetReferenceCount<T>(string path)
    {
        string key = path + "_" + typeof(T).Name;
        if (_resourcesInfoDic.TryGetValue(key, out var resourcesInfoBase))
        {
            if (resourcesInfoBase is ResourcesInfo<T> resourcesInfo)
                return resourcesInfo.referenceCount;
            else
                throw new Exception($"传入的类型是 {typeof(ResourcesInfo<T>).Name} ，但 dic 中 key {key} 记录的类型是 {resourcesInfoBase.GetType()} ");
        }
        throw new Exception($"key {key} 在 dic 中 不存在");
    }
    public int GetReferenceCount(string path, Type type)
    {
        string key = path + "_" + type.Name;
        if (_resourcesInfoDic.TryGetValue(key, out var resourcesInfoBase))
        {
            if (resourcesInfoBase is ResourcesInfo<UnityEngine.Object> resourcesInfo)
                return resourcesInfo.referenceCount;
            else
                throw new Exception($"传入的类型是 {typeof(ResourcesInfo<UnityEngine.Object>).Name} ，但 dic 中 key {key} 记录的类型是 {resourcesInfoBase.GetType()} ");
        }
        throw new Exception($"key {key} 在 dic 中 不存在");
    }
}
