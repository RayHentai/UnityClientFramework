using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class ResourcesManager : SingletonLazyBase<ResourcesManager>
{
    /// <summary>
    /// 记录的非泛型部分:字典里要放不同 T 的记录,只能存父类
    /// </summary>
    private abstract class ResourcesInfoBase
    {
        public int referenceCount;
        // 放在父类:清理没人用的资源时,要在不知道 T 的情况下判断它是不是还在加载
        public bool isLoading;
    }

    /// <summary>
    /// 一条资源记录:资源、加载中挂着的回调、加载协程、引用计数、计数归零时是否立即卸载
    /// </summary>
    private class ResourcesInfo<T> : ResourcesInfoBase
    {
        public T asset;
        public UnityAction<T> callback;
        public Coroutine coroutine;
        public bool isUnloadImmediate;

        /// <summary>
        /// 新建记录就是第一次请求,计数从 1 开始
        /// </summary>
        public ResourcesInfo()
        {
            referenceCount = 1;
        }
    }

    private Dictionary<string, ResourcesInfoBase> _resourcesInfoDic = new();

    // ================= key =================

    /// <summary>
    /// key:路径_类型。加载、卸载、查询都用它拼,保证几处拼法一致
    /// </summary>
    private string GetKey(string path, Type type)
    {
        return path + "_" + type.Name;
    }

    // ================= 同步加载 =================

    /// <summary>
    /// 同步加载:计数加一,返回资源;碰上异步加载中,停掉协程、当场同步加载一次
    /// </summary>
    public T Load<T>(string path) where T : UnityEngine.Object
    {
        string key = GetKey(path, typeof(T));
        if (_resourcesInfoDic.TryGetValue(key, out ResourcesInfoBase resourcesInfoBase))
        {
            if (resourcesInfoBase is not ResourcesInfo<T> resourcesInfo)
            {
                throw new Exception($"传入的类型是 {typeof(ResourcesInfo<T>).Name} ,但 dic 中 key {key} 记录的类型是 {resourcesInfoBase.GetType()} ");
            }
            resourcesInfo.referenceCount++;
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

        ResourcesInfo<T> newInfo = new();
        T newAsset = Resources.Load<T>(path);
        newInfo.asset = newAsset;
        _resourcesInfoDic.Add(key, newInfo);
        return newAsset;
    }

    /// <summary>
    /// 同步加载(类型运行时才知道)
    /// </summary>
    public UnityEngine.Object Load(string path, Type type)
    {
        string key = GetKey(path, type);
        if (_resourcesInfoDic.TryGetValue(key, out ResourcesInfoBase resourcesInfoBase))
        {
            if (resourcesInfoBase is not ResourcesInfo<UnityEngine.Object> resourcesInfo)
            {
                throw new Exception($"传入的类型是 {typeof(ResourcesInfo<UnityEngine.Object>).Name} ,但 dic 中 key {key} 记录的类型是 {resourcesInfoBase.GetType()} ");
            }
            resourcesInfo.referenceCount++;
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

        ResourcesInfo<UnityEngine.Object> newInfo = new();
        UnityEngine.Object newAsset = Resources.Load(path, type);
        newInfo.asset = newAsset;
        _resourcesInfoDic.Add(key, newInfo);
        return newAsset;
    }

    // ================= 异步加载 =================

    /// <summary>
    /// 异步加载:计数加一;加载中就把回调挂上去,已加载就当场回调
    /// </summary>
    public void LoadAsync<T>(string path, UnityAction<T> callback = null) where T : UnityEngine.Object
    {
        string key = GetKey(path, typeof(T));
        if (_resourcesInfoDic.TryGetValue(key, out ResourcesInfoBase resourcesInfoBase))
        {
            if (resourcesInfoBase is not ResourcesInfo<T> resourcesInfo)
            {
                throw new Exception($"传入的类型是 {typeof(ResourcesInfo<T>).Name} ,但 dic 中 key {key} 记录的类型是 {resourcesInfoBase.GetType()} ");
            }
            resourcesInfo.referenceCount++;
            if (resourcesInfo.isLoading)
            {
                resourcesInfo.callback += callback;
                return;
            }
            callback?.Invoke(resourcesInfo.asset);
            return;
        }

        // 一开始就登记(在第一个 yield 之前),同一帧的后续请求才查得到
        ResourcesInfo<T> newInfo = new();
        newInfo.callback += callback;
        _resourcesInfoDic.Add(key, newInfo);
        newInfo.coroutine = MonoManager.Instance.StartCor(LoadAsyncCoroutine<T>(path));
    }

    private IEnumerator LoadAsyncCoroutine<T>(string path) where T : UnityEngine.Object
    {
        string key = GetKey(path, typeof(T));
        ResourcesInfo<T> resourcesInfo = _resourcesInfoDic[key] as ResourcesInfo<T>;
        resourcesInfo.isLoading = true;
        ResourceRequest request = Resources.LoadAsync<T>(path);
        yield return request;

        T asset = request.asset as T;
        resourcesInfo.asset = asset;
        // 加载途中所有人都释放了,并且要求立即卸载
        if (resourcesInfo.referenceCount == 0 && resourcesInfo.isUnloadImmediate)
        {
            UnloadIfUnreferenced(key, resourcesInfo);
            yield break;
        }
        resourcesInfo.isLoading = false;
        resourcesInfo.callback?.Invoke(asset);
        resourcesInfo.callback = null;
        resourcesInfo.coroutine = null;
    }

    /// <summary>
    /// 异步加载(类型运行时才知道)
    /// </summary>
    public void LoadAsync(string path, Type type, UnityAction<UnityEngine.Object> callback = null)
    {
        string key = GetKey(path, type);
        if (_resourcesInfoDic.TryGetValue(key, out ResourcesInfoBase resourcesInfoBase))
        {
            if (resourcesInfoBase is not ResourcesInfo<UnityEngine.Object> resourcesInfo)
            {
                throw new Exception($"传入的类型是 {typeof(ResourcesInfo<UnityEngine.Object>).Name} ,但 dic 中 key {key} 记录的类型是 {resourcesInfoBase.GetType()} ");
            }
            resourcesInfo.referenceCount++;
            if (resourcesInfo.isLoading)
            {
                resourcesInfo.callback += callback;
                return;
            }
            callback?.Invoke(resourcesInfo.asset);
            return;
        }

        ResourcesInfo<UnityEngine.Object> newInfo = new();
        newInfo.callback += callback;
        _resourcesInfoDic.Add(key, newInfo);
        newInfo.coroutine = MonoManager.Instance.StartCor(LoadAsyncCoroutine(path, type));
    }

    private IEnumerator LoadAsyncCoroutine(string path, Type type)
    {
        string key = GetKey(path, type);
        ResourcesInfo<UnityEngine.Object> resourcesInfo = _resourcesInfoDic[key] as ResourcesInfo<UnityEngine.Object>;
        resourcesInfo.isLoading = true;
        ResourceRequest request = Resources.LoadAsync(path, type);
        yield return request;

        UnityEngine.Object asset = request.asset;
        resourcesInfo.asset = asset;
        // 加载途中所有人都释放了,并且要求立即卸载
        if (resourcesInfo.referenceCount == 0 && resourcesInfo.isUnloadImmediate)
        {
            UnloadIfUnreferenced(key, resourcesInfo);
            yield break;
        }
        resourcesInfo.isLoading = false;
        resourcesInfo.callback?.Invoke(asset);
        resourcesInfo.callback = null;
        resourcesInfo.coroutine = null;
    }

    // ================= 卸载 =================

    /// <summary>
    /// 卸载:计数减一;加载中只撤掉自己的回调,卸不卸留给协程加载完时判断;已加载的计数归零且要求立即卸载才卸
    /// </summary>
    public void UnloadAsset<T>(string path, bool isUnloadImmediately = false, UnityAction<T> callback = null) where T : UnityEngine.Object
    {
        string key = GetKey(path, typeof(T));
        if (!_resourcesInfoDic.TryGetValue(key, out ResourcesInfoBase resourcesInfoBase))
        {
            throw new Exception($"key {key} 在 dic 中 不存在");
        }
        if (resourcesInfoBase is not ResourcesInfo<T> resourcesInfo)
        {
            throw new Exception($"传入的类型是 {typeof(ResourcesInfo<T>).Name} ,但 dic 中 key {key} 记录的类型是 {resourcesInfoBase.GetType()} ");
        }

        // 计数为 0 的记录还留着(没要求立即卸载),再释放说明加载、卸载没成对
        if (resourcesInfo.referenceCount == 0)
        {
            throw new Exception($"key {key} 引用计数已经为 0,加载/卸载资源 没有成对");
        }
        resourcesInfo.isUnloadImmediate = isUnloadImmediately;
        resourcesInfo.referenceCount--;
        if (resourcesInfo.isLoading)
        {
            // 撤回调要传加载时的同一个委托实例,现场新写的 lambda 撤不掉
            resourcesInfo.callback -= callback;
            return;
        }
        UnloadIfUnreferenced(key, resourcesInfo);
    }

    /// <summary>
    /// 卸载(类型运行时才知道)
    /// </summary>
    public void UnloadAsset(string path, Type type, bool isUnloadImmediately = false, UnityAction<UnityEngine.Object> callback = null)
    {
        string key = GetKey(path, type);
        if (!_resourcesInfoDic.TryGetValue(key, out ResourcesInfoBase resourcesInfoBase))
        {
            throw new Exception($"key {key} 在 dic 中 不存在");
        }
        if (resourcesInfoBase is not ResourcesInfo<UnityEngine.Object> resourcesInfo)
        {
            throw new Exception($"传入的类型是 {typeof(ResourcesInfo<UnityEngine.Object>).Name} ,但 dic 中 key {key} 记录的类型是 {resourcesInfoBase.GetType()} ");
        }

        // 计数为 0 的记录还留着(没要求立即卸载),再释放说明加载、卸载没成对
        if (resourcesInfo.referenceCount == 0)
        {
            throw new Exception($"key {key} 引用计数已经为 0,加载/卸载资源 没有成对");
        }
        resourcesInfo.isUnloadImmediate = isUnloadImmediately;
        resourcesInfo.referenceCount--;
        if (resourcesInfo.isLoading)
        {
            resourcesInfo.callback -= callback;
            return;
        }
        UnloadIfUnreferenced(key, resourcesInfo);
    }

    /// <summary>
    /// 清掉计数为 0 的记录,再让 Unity 卸载没被引用的资源;卸完调用 callback
    /// </summary>
    public void UnloadUnusedAssets(UnityAction callback = null)
    {
        MonoManager.Instance.StartCor(UnloadUnusedAssetsCoroutine(callback));
    }

    private IEnumerator UnloadUnusedAssetsCoroutine(UnityAction callback = null)
    {
        List<string> keysToRemove = new();
        foreach (KeyValuePair<string, ResourcesInfoBase> pair in _resourcesInfoDic)
        {
            // 还在加载的不清:清掉以后协程还在跑。加载完时如果计数为 0 且要求立即卸载, 
            // 它会按 key 撤记录、卸资源,撤掉的可能是之后新建的同名记录,卸掉的是那条记录正在用的资源
            // 另外,不立即卸载时,加载出来的资源只写进那条已经被清掉的旧记录,管理器管不到它,
            // 要等下一次 Resources.UnloadUnusedAssets() 才会被卸掉
            if (pair.Value.referenceCount == 0 && !pair.Value.isLoading)
            {
                keysToRemove.Add(pair.Key);
            }
        }
        foreach (string key in keysToRemove)
        {
            _resourcesInfoDic.Remove(key);
        }
        AsyncOperation operation = Resources.UnloadUnusedAssets();
        yield return operation;
        callback?.Invoke();
    }

    // ================= 引用计数查询 =================

    public int GetReferenceCount<T>(string path)
    {
        string key = GetKey(path, typeof(T));
        if (!_resourcesInfoDic.TryGetValue(key, out ResourcesInfoBase resourcesInfoBase))
        {
            throw new Exception($"key {key} 在 dic 中 不存在");
        }
        if (resourcesInfoBase is not ResourcesInfo<T> resourcesInfo)
        {
            throw new Exception($"传入的类型是 {typeof(ResourcesInfo<T>).Name} ,但 dic 中 key {key} 记录的类型是 {resourcesInfoBase.GetType()} ");
        }
        return resourcesInfo.referenceCount;
    }

    public int GetReferenceCount(string path, Type type)
    {
        string key = GetKey(path, type);
        if (!_resourcesInfoDic.TryGetValue(key, out ResourcesInfoBase resourcesInfoBase))
        {
            throw new Exception($"key {key} 在 dic 中 不存在");
        }
        if (resourcesInfoBase is not ResourcesInfo<UnityEngine.Object> resourcesInfo)
        {
            throw new Exception($"传入的类型是 {typeof(ResourcesInfo<UnityEngine.Object>).Name} ,但 dic 中 key {key} 记录的类型是 {resourcesInfoBase.GetType()} ");
        }
        return resourcesInfo.referenceCount;
    }

    // ================= 共用 =================

    /// <summary>
    /// 计数为 0 且要求立即卸载时:卸资源(预设体和组件不能单独卸,交给 UnloadUnusedAssets)、撤记录
    /// </summary>
    private void UnloadIfUnreferenced<T>(string key, ResourcesInfo<T> info) where T : UnityEngine.Object
    {
        if (info.referenceCount != 0 || !info.isUnloadImmediate)
        {
            return;
        }
        if (info.asset is not (GameObject or Component))
        {
            Resources.UnloadAsset(info.asset);
        }
        _resourcesInfoDic.Remove(key);
    }
}