using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class ABManager : SingletonLazyBase<ABManager>
{
    private class BundleInfo
    {
        public AssetBundle bundle;
        public bool isLoading;
        public AssetBundleCreateRequest request;
    }
    private Dictionary<string, BundleInfo> _bundleDic = new();
    private HashSet<Coroutine> _runningCoroutines = new();
    private AssetBundle _mainBundle;
    private AssetBundleManifest _manifest;
    private readonly string _bundleDirectory = Application.streamingAssetsPath + "/";
#if UNITY_IOS
    private const string MainBundleName = "IOS";
#elif UNITY_ANDROID
    private const string MainBundleName = "Android";
#else
    private const string MainBundleName = "PC";
#endif
    private void LoadMainBundle()
    {
        if (_mainBundle == null)
        {
            _mainBundle = AssetBundle.LoadFromFile(_bundleDirectory + MainBundleName);
            if (_mainBundle != null)
                _manifest = _mainBundle.LoadAsset<AssetBundleManifest>("AssetBundleManifest");
            else
                throw new Exception($"路径 {_bundleDirectory + MainBundleName} 下 不存在 主包");
        }
    }

    private AssetBundle LoadBundle(string bundleName)
    {
        if (_bundleDic.TryGetValue(bundleName, out var bundleInfo))
        {
            if (bundleInfo.isLoading)
            {
                AssetBundle bundle = bundleInfo.request.assetBundle;
                if (bundle != null)
                {
                    bundleInfo.bundle = bundle;
                    bundleInfo.isLoading = false;
                    return bundle;
                }
                else
                {
                    _bundleDic.Remove(bundleName);
                    throw new Exception($"包 {bundleName} 异步加载 的 请求失败");
                }
            }
            else
                return bundleInfo.bundle;
        }
        else
        {
            BundleInfo info = new();
            info.bundle = AssetBundle.LoadFromFile(_bundleDirectory + bundleName);
            if (info.bundle != null)
            {
                _bundleDic.Add(bundleName, info);
                return info.bundle;
            }
            throw new Exception($"包名 {bundleName} 输入错误 不存在名为 {bundleName} 的包");
        }
    }
    private void LoadBundles(string bundleName)
    {
        string[] dependencies = _manifest.GetAllDependencies(bundleName);
        foreach (string dependency in dependencies)
            LoadBundle(dependency);
        LoadBundle(bundleName);
    }
    public T Load<T>(string bundleName, string assetName) where T : UnityEngine.Object
    {
        LoadMainBundle();
        LoadBundles(bundleName);
        return _bundleDic[bundleName].bundle.LoadAsset<T>(assetName);
    }
    public UnityEngine.Object Load(string bundleName, string assetName, Type type)
    {
        LoadMainBundle();
        LoadBundles(bundleName);
        return _bundleDic[bundleName].bundle.LoadAsset(assetName, type);
    }
    private IEnumerator LoadBundleAsyncCoroutine(string bundleName)
    {
        if (_bundleDic.TryGetValue(bundleName, out var bundleInfo))
        {
            if (bundleInfo.isLoading)
            {
                while (_bundleDic.TryGetValue(bundleName, out var info) && info.isLoading)
                    yield return null;
                if (!_bundleDic.ContainsKey(bundleName))
                    throw new Exception($"包 {bundleName} 异步加载 的 请求失败");
            }
            else
                yield break;
        }
        else
        {
            BundleInfo info = new BundleInfo();
            AssetBundleCreateRequest request = AssetBundle.LoadFromFileAsync(_bundleDirectory + bundleName);
            info.isLoading = true;
            info.request = request;
            _bundleDic.Add(bundleName, info);
            yield return request;
            if (!_bundleDic.ContainsKey(bundleName))
                throw new Exception($"包 {bundleName} 异步加载 的 请求失败");
            if (info.request.assetBundle == null)
            {
                _bundleDic.Remove(bundleName);
                throw new Exception($"包名 {bundleName} 输入错误 不存在名为 {bundleName} 的包");
            }
            info.isLoading = false;
            info.bundle = request.assetBundle;
        }
    }
    public void LoadAsync<T>(string bundleName, string assetName, UnityAction<T> callback = null) where T : UnityEngine.Object
    {
        Coroutine coroutine = null;
        coroutine = MonoManager.Instance.StartCor(LoadAndUntrackCoroutine());
        _runningCoroutines.Add(coroutine);
        IEnumerator LoadAndUntrackCoroutine()
        {
            yield return LoadAsyncCoroutine(bundleName, assetName, callback);
            _runningCoroutines.Remove(coroutine);
        }
    }
    private IEnumerator LoadAsyncCoroutine<T>(string bundleName, string assetName, UnityAction<T> callback = null) where T : UnityEngine.Object
    {
        LoadMainBundle();
        string[] dependencies = _manifest.GetAllDependencies(bundleName);
        foreach (string dependency in dependencies)
            yield return LoadBundleAsyncCoroutine(dependency);
        yield return LoadBundleAsyncCoroutine(bundleName);
        AssetBundleRequest abr = _bundleDic[bundleName].bundle.LoadAssetAsync<T>(assetName);
        yield return abr;
        callback?.Invoke(abr.asset as T);
    }
    public void LoadAsync(string bundleName, string assetName, Type type, UnityAction<UnityEngine.Object> callback = null)
    {
        Coroutine coroutine = null;
        coroutine = MonoManager.Instance.StartCor(LoadAndUntrackCoroutine());
        _runningCoroutines.Add(coroutine);
        IEnumerator LoadAndUntrackCoroutine()
        {
            yield return LoadAsyncCoroutine(bundleName, assetName, type, callback);
            _runningCoroutines.Remove(coroutine);
        }
    }
    private IEnumerator LoadAsyncCoroutine(string bundleName, string assetName, Type type, UnityAction<UnityEngine.Object> callback = null)
    {
        LoadMainBundle();
        string[] dependencies = _manifest.GetAllDependencies(bundleName);
        foreach (string dependency in dependencies)
            yield return LoadBundleAsyncCoroutine(dependency);
        yield return LoadBundleAsyncCoroutine(bundleName);
        AssetBundleRequest abr = _bundleDic[bundleName].bundle.LoadAssetAsync(assetName, type);
        yield return abr;
        callback?.Invoke(abr.asset);
    }
    public void UnloadBundle(string bundleName, bool unloadAllLoadedObjects = false)
    {
        if (_bundleDic.TryGetValue(bundleName, out var bundleInfo))
        {
            if (bundleInfo.isLoading)
            {
                AssetBundle bundle = bundleInfo.request.assetBundle;
                if (bundle != null)
                {
                    bundle.Unload(unloadAllLoadedObjects);
                    _bundleDic.Remove(bundleName);
                }
                else
                {
                    _bundleDic.Remove(bundleName);
                    throw new Exception($"包 {bundleName} 异步加载 的 请求失败 导致 强制卸载 失败");
                }

            }
            else
            {
                bundleInfo.bundle.Unload(unloadAllLoadedObjects);
                _bundleDic.Remove(bundleName);
            }
        }
        else
            throw new Exception($"key {bundleName} 在 dic 中 不存在");
    }
    public void UnloadAllBundles(bool unloadAllLoadedObjects = false)
    {
        foreach (var coroutine in _runningCoroutines)
            MonoManager.Instance.StopCor(coroutine);
        _runningCoroutines.Clear();
        foreach (var bundleInfo in _bundleDic.Values)
        {
            if (bundleInfo.isLoading)
                _ = bundleInfo.request.assetBundle;
        }
        AssetBundle.UnloadAllAssetBundles(unloadAllLoadedObjects);
        _bundleDic.Clear();
        _mainBundle = null;
        _manifest = null;
    }
}