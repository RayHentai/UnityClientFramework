using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class ABManager : SingletonLazyBase<ABManager>
{
    private Dictionary<string, AssetBundle> _bundleDic = new();
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
    private void LoadBundle(string bundleName)
    {
        if (_mainBundle == null) 
        {
            _mainBundle = AssetBundle.LoadFromFile(_bundleDirectory + MainBundleName);
            if (_mainBundle != null) 
                _manifest = _mainBundle.LoadAsset<AssetBundleManifest>("AssetBundleManifest");
            else
                throw new Exception($"路径 {_bundleDirectory + MainBundleName} 下 不存在 主包");
        }
        string[] dependencies = _manifest.GetAllDependencies(bundleName);
        foreach (string dependency in dependencies)
        {
            if (!_bundleDic.ContainsKey(dependency))
            {
                AssetBundle dependencyBundle = AssetBundle.LoadFromFile(_bundleDirectory + dependency);
                if (dependencyBundle != null)
                    _bundleDic.Add(dependency, dependencyBundle);
                else
                    throw new Exception($"不存在 名为 {dependency} 的依赖包");
            }
        }
        if (!_bundleDic.ContainsKey(bundleName))
        {
            AssetBundle targetBundle = AssetBundle.LoadFromFile(_bundleDirectory + bundleName);
            if (targetBundle != null)
                _bundleDic.Add(bundleName, targetBundle);
            else 
                throw new Exception($"包名 {bundleName} 输入错误 不存在名为 {bundleName} 的包");
        }
    }
    public T Load<T>(string bundleName, string assetName) where T : UnityEngine.Object
    {
        LoadBundle(bundleName);
        return _bundleDic[bundleName].LoadAsset<T>(assetName);
    }
    public UnityEngine.Object Load(string bundleName, string assetName, Type type)
    {
        LoadBundle(bundleName);
        return _bundleDic[bundleName].LoadAsset(assetName, type);
    }
    public void LoadAsync<T>(string bundleName, string assetName, UnityAction<T> callback = null) where T : UnityEngine.Object
    {
        MonoManager.Instance.StartCor(LoadAsyncCoroutine(bundleName, assetName, callback));
    }
    private IEnumerator LoadAsyncCoroutine<T>(string bundleName, string assetName, UnityAction<T> callback = null) where T : UnityEngine.Object
    {
        LoadBundle(bundleName);
        AssetBundleRequest abr = _bundleDic[bundleName].LoadAssetAsync<T>(assetName);
        yield return abr;
        callback?.Invoke(abr.asset as T);
    }
    public void LoadAsync(string bundleName, string assetName, Type type, UnityAction<UnityEngine.Object> callback = null)
    {
        MonoManager.Instance.StartCor(LoadAsyncCoroutine(bundleName, assetName, type, callback));
    }
    private IEnumerator LoadAsyncCoroutine(string bundleName, string assetName, Type type, UnityAction<UnityEngine.Object> callback = null)
    {
        LoadBundle(bundleName);
        AssetBundleRequest abr = _bundleDic[bundleName].LoadAssetAsync(assetName, type);
        yield return abr;
        callback?.Invoke(abr.asset);
    }
    public void UnloadBundle(string bundleName, bool unloadAllLoadedObjects = false)
    {
        if (_bundleDic.TryGetValue(bundleName, out var assetBundle))
        {
            assetBundle.Unload(unloadAllLoadedObjects);
            _bundleDic.Remove(bundleName);
        }
        else
            throw new Exception($"key {bundleName} 在 dic 中 不存在");
    }
    public void UnloadAllBundles(bool unloadAllLoadedObjects = false)
    {
        AssetBundle.UnloadAllAssetBundles(unloadAllLoadedObjects);
        _bundleDic.Clear();
        _mainBundle = null;
        _manifest = null;
    }
}