using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Events;
using UnityEngine.ResourceManagement.AsyncOperations;

public class AddressablesManager : SingletonLazyBase<AddressablesManager>
{
    /// <summary>
    /// 一条资源记录:句柄 + 引用计数
    /// 同一个 key 的请求共用一个句柄,Addressables 内部只记了 1 次,所以有几个人在用要自己记
    /// </summary>
    private class AddressablesInfo
    {
        public AsyncOperationHandle handle;
        public int referenceCount;

        public AddressablesInfo(AsyncOperationHandle handle)
        {
            this.handle = handle;
            referenceCount = 1;
        }
    }

    private Dictionary<string, AddressablesInfo> _infoDic = new();

    // ================= key =================

    /// <summary>
    /// 单个加载的 key:名字_类型
    /// </summary>
    private string GetKey(string name, Type type)
    {
        return name + "_" + type.Name;
    }

    /// <summary>
    /// 多条件的 key:条件_条件_…_类型_合并模式(以合并模式结尾,和单个的不会撞)
    /// </summary>
    private string GetKey(string[] keys, Type type, Addressables.MergeMode mode)
    {
        return string.Join("_", keys) + "_" + type.Name + "_" + mode;
    }

    // ================= 单个 =================

    /// <summary>
    /// 同步加载一个:成功返回资源(计数加一),失败返回 null
    /// </summary>
    public T Load<T>(string name) where T : UnityEngine.Object
    {
        string key = GetKey(name, typeof(T));
        if (_infoDic.TryGetValue(key, out AddressablesInfo info))
        {
            if (!info.handle.IsDone)
            {
                // 第一次加载的转接头会当场执行:失败时由它打异步日志、撤记录
                info.handle.WaitForCompletion();
            }
            if (info.handle.Status != AsyncOperationStatus.Succeeded)
            {
                Debug.LogError($"资源 {name} 在方法 {nameof(Load)} 中 加载失败");
                return null;
            }
            info.referenceCount++;
            return info.handle.Result as T;
        }

        AsyncOperationHandle<T> newHandle = Addressables.LoadAssetAsync<T>(name);
        newHandle.WaitForCompletion();
        if (newHandle.Status != AsyncOperationStatus.Succeeded)
        {
            Debug.LogError($"资源 {name} 在方法 {nameof(Load)} 中 强制 执行异步加载 失败");
            // 从没进过字典,当场释放
            Addressables.Release(newHandle);
            return null;
        }
        _infoDic.Add(key, new AddressablesInfo(newHandle));
        return newHandle.Result;
    }

    /// <summary>
    /// 异步加载一个:成功回调收到资源,失败收到 null
    /// </summary>
    public void LoadAsync<T>(string name, UnityAction<T> callback = null) where T : UnityEngine.Object
    {
        string key = GetKey(name, typeof(T));
        if (_infoDic.TryGetValue(key, out AddressablesInfo info))
        {
            // 发起时就加一:还在加载时有人释放,才知道还有别人在等
            info.referenceCount++;
            AsyncOperationHandle<T> typeHandle = info.handle.Convert<T>();
            if (!info.handle.IsDone)
            {
                info.handle.Completed += _ => InvokeCallback(typeHandle, callback);
                return;
            }
            InvokeCallback(typeHandle, callback);
            return;
        }

        AsyncOperationHandle<T> newHandle = Addressables.LoadAssetAsync<T>(name);
        _infoDic.Add(key, new AddressablesInfo(newHandle));
        newHandle.Completed += h =>
        {
            // 打日志、撤记录只由第一次加载的转接头做
            if (h.Status != AsyncOperationStatus.Succeeded)
            {
                _infoDic.Remove(key);
                Debug.LogError($"资源 {name} 异步加载失败");
            }
            InvokeCallback(h, callback);
        };
    }

    /// <summary>
    /// 转接头:成功交资源,失败交 null
    /// </summary>
    private void InvokeCallback<T>(AsyncOperationHandle<T> handle, UnityAction<T> callback) where T : UnityEngine.Object
    {
        if (handle.Status != AsyncOperationStatus.Succeeded)
        {
            callback?.Invoke(null);
            return;
        }
        callback?.Invoke(handle.Result);
    }

    /// <summary>
    /// 释放一个(计数减一,减到 0 才真正释放)
    /// </summary>
    public void Release<T>(string name)
    {
        ReleaseByKey(GetKey(name, typeof(T)));
    }

    // ================= 多条件 =================

    /// <summary>
    /// 同步加载一批:成功返回这一批的副本(计数加一),失败返回 null
    /// </summary>
    public IList<T> LoadAssets<T>(Addressables.MergeMode mode, params string[] keys) where T : UnityEngine.Object
    {
        string key = GetKey(keys, typeof(T), mode);
        if (_infoDic.TryGetValue(key, out AddressablesInfo info))
        {
            if (!info.handle.IsDone)
            {
                // 第一次加载的转接头会当场执行:失败时由它打异步日志、撤记录
                info.handle.WaitForCompletion();
            }
            if (info.handle.Status != AsyncOperationStatus.Succeeded)
            {
                Debug.LogError($"资源 {key} 在方法 {nameof(LoadAssets)} 中 加载失败");
                return null;
            }
            info.referenceCount++;
            return new List<T>(info.handle.Convert<IList<T>>().Result);
        }

        AsyncOperationHandle<IList<T>> newHandle = Addressables.LoadAssetsAsync<T>(new List<string>(keys), null, mode);
        newHandle.WaitForCompletion();
        if (newHandle.Status != AsyncOperationStatus.Succeeded)
        {
            Debug.LogError($"资源 {key} 在方法 {nameof(LoadAssets)} 中 强制 执行异步加载 失败");
            // 从没进过字典,当场释放
            Addressables.Release(newHandle);
            return null;
        }
        _infoDic.Add(key, new AddressablesInfo(newHandle));
        return new List<T>(newHandle.Result);
    }

    /// <summary>
    /// 异步加载一批
    /// callback:每个资源调一次;completeCallback:整批结束调一次,成功交副本,失败交 null
    /// </summary>
    public void LoadAssetsAsync<T>(Addressables.MergeMode mode, UnityAction<T> callback, UnityAction<IList<T>> completeCallback, params string[] keys) where T : UnityEngine.Object
    {
        string key = GetKey(keys, typeof(T), mode);
        if (_infoDic.TryGetValue(key, out AddressablesInfo info))
        {
            // 发起时就加一
            info.referenceCount++;
            AsyncOperationHandle<IList<T>> typeHandle = info.handle.Convert<IList<T>>();
            if (!info.handle.IsDone)
            {
                info.handle.Completed += _ => InvokeCallbacks(typeHandle, callback, completeCallback);
                return;
            }
            InvokeCallbacks(typeHandle, callback, completeCallback);
            return;
        }

        // 逐个回调交给 Addressables:每加载完一个资源调一次
        AsyncOperationHandle<IList<T>> newHandle = Addressables.LoadAssetsAsync<T>(new List<string>(keys), obj => callback?.Invoke(obj), mode);
        _infoDic.Add(key, new AddressablesInfo(newHandle));
        newHandle.Completed += h =>
        {
            // 打日志、撤记录只由第一次加载的转接头做
            if (h.Status != AsyncOperationStatus.Succeeded)
            {
                _infoDic.Remove(key);
                Debug.LogError($"资源 {key} 异步加载失败");
            }
            // 逐个回调 Addressables 已经调过了,这里传 null,免得每个资源调两次
            InvokeCallbacks(h, null, completeCallback);
        };
    }

    /// <summary>
    /// 转接头:先看状态。成功:逐个调用 + 整批交副本;失败:只给整批回调交 null
    /// </summary>
    private void InvokeCallbacks<T>(AsyncOperationHandle<IList<T>> handle, UnityAction<T> callback, UnityAction<IList<T>> completeCallback) where T : UnityEngine.Object
    {
        if (handle.Status != AsyncOperationStatus.Succeeded)
        {
            completeCallback?.Invoke(null);
            return;
        }
        if (callback != null)
        {
            foreach (T item in handle.Result)
            {
                callback(item);
            }
        }
        // 交副本:调用方改它,影响不到句柄里那份
        completeCallback?.Invoke(new List<T>(handle.Result));
    }

    /// <summary>
    /// 释放一批(计数减一,减到 0 才真正释放)
    /// </summary>
    public void ReleaseAssets<T>(Addressables.MergeMode mode, params string[] keys) where T : UnityEngine.Object
    {
        ReleaseByKey(GetKey(keys, typeof(T), mode));
    }

    // ================= 释放(两种共用) =================

    /// <summary>
    /// 按 key 释放:没记录 throw;计数减一,还有人在用就返回;
    /// 减到 0 才真正释放:加载中的先强制完成,成功才释放、撤记录;已完成的直接撤记录、释放
    /// </summary>
    private void ReleaseByKey(string key)
    {
        if (!_infoDic.TryGetValue(key, out AddressablesInfo info))
        {
            throw new Exception($"资源 {key} 释放失败 dic 中 未记录该资源");
        }

        info.referenceCount--;
        if (info.referenceCount > 0)
        {
            // 还有人在用:只减计数,加载中的也不用强制完成
            return;
        }

        AsyncOperationHandle handle = info.handle;
        if (!handle.IsDone)
        {
            handle.WaitForCompletion();
            // 失败时,第一次加载的转接头已经撤了记录,这里不再撤
            if (handle.Status != AsyncOperationStatus.Succeeded)
            {
                return;
            }
            // 强制完成时回调当场执行,回调里可能又有人来要同一个资源,计数从 0 回到 1,这时不能释放
            if (info.referenceCount > 0)
            {
                return;
            }
        }
        _infoDic.Remove(key);
        handle.Release();
    }

    /// <summary>
    /// 清空:不管计数,全部释放。拷记录 → 清字典 → 逐个释放(加载中的先强制完成)→ 卸掉没人用的资源
    /// </summary>
    public void ReleaseAll()
    {
        // 拷一份再遍历:强制完成时回调会当场执行、可能改字典,直接遍历字典会抛 InvalidOperationException
        List<AddressablesInfo> infos = new(_infoDic.Values);
        // 先清字典的理由是 回调中可能会再加载一个资源 如果后清 这个资源的记录也会一起被清 变成孤儿
        _infoDic.Clear();
        foreach (AddressablesInfo info in infos)
        {
            AsyncOperationHandle handle = info.handle;
            if (handle.IsDone)
            {
                handle.Release();
                continue;
            }
            handle.WaitForCompletion();
            if (handle.Status == AsyncOperationStatus.Succeeded)
            {
                handle.Release();
            }
        }
        Resources.UnloadUnusedAssets();
        GC.Collect();
    }
}