using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Events;
using UnityEngine.ResourceManagement.AsyncOperations;

public class AddressablesManager : SingletonLazyBase<AddressablesManager>
{
    private Dictionary<string, AsyncOperationHandle> _handleDic = new();
    private string GetKey(string name, Type type) 
    {
        return name + "_" + type.Name;
    }
    public T Load<T>(string name) where T : UnityEngine.Object
    {
        string key = GetKey(name, typeof(T));
        if (_handleDic.TryGetValue(key, out var handle))
        {
            if (!handle.IsDone)
            {
                handle.WaitForCompletion();
                if (handle.Status == AsyncOperationStatus.Succeeded)
                    return handle.Result as T;
                else
                {
                    Debug.LogError($"资源 {name} 在方法 {nameof(Load)} 中 强制 执行异步加载 失败");
                    return null;
                }
            }
            else 
            {
                if (handle.Status != AsyncOperationStatus.Succeeded) 
                {
                    Debug.LogError($"资源 {name} 在方法 {nameof(Load)} 中 加载失败");
                    return null;
                }
                return handle.Result as T;
            }
        }
        else
        {
            AsyncOperationHandle<T> newHandle = Addressables.LoadAssetAsync<T>(name);
            newHandle.WaitForCompletion();
            if (newHandle.Status == AsyncOperationStatus.Succeeded)
            {
                _handleDic.Add(key, newHandle);
                return newHandle.Result;
            }
            else 
            {
                Debug.LogError($"资源 {name} 在方法 {nameof(Load)} 中 强制 执行异步加载 失败");
                Addressables.Release(newHandle);
                return null;
            }
        }
    }
    private void InvokeCallback<T>(AsyncOperationHandle<T> handle, UnityAction<T> callback) where T : UnityEngine.Object
    {
        if (handle.Status == AsyncOperationStatus.Succeeded)
            callback?.Invoke(handle.Result);
        else 
            callback?.Invoke(null);
    }
    public void LoadAsync<T>(string name, UnityAction<T> callback = null) where T : UnityEngine.Object
    {
        string key = GetKey(name, typeof(T));
        if (_handleDic.TryGetValue(key, out var handle))
        {
            AsyncOperationHandle<T> typeHandle = handle.Convert<T>();
            if (!handle.IsDone)
                handle.Completed += handle => InvokeCallback<T>(typeHandle, callback);
            else 
                InvokeCallback<T>(typeHandle, callback);
        }
        else
        {
            AsyncOperationHandle<T> newHandle = Addressables.LoadAssetAsync<T>(name);
            _handleDic.Add(key, newHandle);
            newHandle.Completed += handle =>
            {
                if (!(handle.Status == AsyncOperationStatus.Succeeded)) 
                {
                    _handleDic.Remove(key);
                    Debug.LogError($"资源 {name} 异步加载失败");
                }
                InvokeCallback<T>(handle, callback);
            };
        }
    }
    public void Release<T>(string name) 
    {
        string key = GetKey(name, typeof(T));
        if (_handleDic.TryGetValue(key, out var handle))
        {
            if (!handle.IsDone)
            {
                handle.WaitForCompletion();
                if (handle.Status == AsyncOperationStatus.Succeeded) 
                {
                    handle.Release();
                    _handleDic.Remove(key);
                }
            }
            else
            {
                _handleDic.Remove(key);
                handle.Release();
            }
        }
        else
            throw new Exception($"资源 {name} 释放失败 dic 中 未记录该资源");
    }
    public void ReleaseAll() 
    {
        List<AsyncOperationHandle> handles = new(_handleDic.Values);
        _handleDic.Clear();
        foreach (var handle in handles)
        {
            if (!handle.IsDone)
            {
                handle.WaitForCompletion();
                if (handle.Status == AsyncOperationStatus.Succeeded)
                    handle.Release();
            }
            else 
                handle.Release();
        }
        Resources.UnloadUnusedAssets();
        GC.Collect();
    }
}
