using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using TargetManager = ResourcesManager;   // 被测的管理器类名,改名时只改这一行

/// <summary>
/// ResourcesManager:UnloadUnusedAssets 清掉加载中记录的 bug 的回归测试
/// 准备:Inspector 里把 _materialPath 填成 Resources 文件夹里一个材质的路径(不带扩展名,例如 "Test/Red")
/// 场景:A 异步加载中立即卸载(计数 0、要求立即卸载)→ 同一帧 UnloadUnusedAssets → 同一帧 B 异步要同一个资源
/// 修之前:记录被清掉,B 新建记录;A 的旧协程加载完按 key 撤掉 B 的记录、卸掉 B 正在用的资源,B 之后释放会 throw
/// 修之后:加载中的记录不清,B 挂在同一条记录上,计数 1,能正常释放
/// 读私有字段 _resourcesInfoDic 和记录的 referenceCount、isLoading 用的是反射,改名的话这里也要改
/// </summary>
public class ResourcesManagerBugTest : MonoBehaviour
{
    private const string NoThrow = "没有 throw";

    [SerializeField] private string _materialPath = "";

    private int _pass;
    private int _fail;

    private TargetManager Mgr => TargetManager.Instance;

    private IEnumerator Start()
    {
        yield return null;
        if (string.IsNullOrEmpty(_materialPath))
        {
            Debug.LogError("先在 Inspector 里填 _materialPath");
            yield break;
        }

        string key = _materialPath + "_" + nameof(Material);

        // A 异步加载,还在加载中就立即卸载
        Mgr.LoadAsync<Material>(_materialPath);
        bool loading = IsLoading(key);
        Mgr.UnloadAsset<Material>(_materialPath, true);

        // 同一帧清理没人用的资源
        Mgr.UnloadUnusedAssets();
        bool recordKept = HasRecord(key);

        // 同一帧 B 来要同一个资源
        Material gotB = null;
        Mgr.LoadAsync<Material>(_materialPath, m => gotB = m);

        for (int i = 0; i < 60; i++)
        {
            yield return null;
        }

        if (!loading)
        {
            Debug.LogWarning("[OBSERVE] 前提不成立:A 发起后已经加载完,没造出\"加载中\"");
        }
        Check("B1 加载中的记录没被 UnloadUnusedAssets 清掉", recordKept, $"清理后有记录:{recordKept}");
        Check("B2 B 拿到材质;记录还在、计数 1", gotB != null && HasRecord(key) && CountOf(key) == 1, $"B {(gotB == null ? "null" : gotB.name)};有记录:{HasRecord(key)};计数 {CountOf(key)}");
        string r3 = TryRun(() => Mgr.UnloadAsset<Material>(_materialPath));
        Check("B3 B 正常释放:不 throw", r3 == NoThrow, r3);

        Debug.Log($"===== R bug 回归结束:PASS {_pass} / FAIL {_fail} =====");
    }

    // ---------------- 工具 ----------------
    private IDictionary InfoDic()
    {
        FieldInfo field = typeof(TargetManager).GetField("_resourcesInfoDic", BindingFlags.NonPublic | BindingFlags.Instance);
        return field?.GetValue(Mgr) as IDictionary;
    }

    private object InfoOf(string key)
    {
        IDictionary dic = InfoDic();
        return dic != null && dic.Contains(key) ? dic[key] : null;
    }

    private bool HasRecord(string key)
    {
        return InfoOf(key) != null;
    }

    private int CountOf(string key)
    {
        object info = InfoOf(key);
        return info == null ? -1 : (int)info.GetType().GetField("referenceCount").GetValue(info);
    }

    private bool IsLoading(string key)
    {
        object info = InfoOf(key);
        return info != null && (bool)info.GetType().GetField("isLoading").GetValue(info);
    }

    private string TryRun(Action action)
    {
        try
        {
            action();
            return NoThrow;
        }
        catch (Exception e)
        {
            return $"throw {e.GetType().Name}:{e.Message}";
        }
    }

    private void Check(string id, bool ok, string detail)
    {
        if (ok)
        {
            _pass++;
            Debug.Log($"[PASS] {id} | {detail}");
        }
        else
        {
            _fail++;
            Debug.LogError($"[FAIL] {id} | {detail}");
        }
    }
}