using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using Object = UnityEngine.Object;
using TargetManager = AddressablesManager;   // 被测的管理器类名,改名时只改这一行

/// <summary>
/// AddressablesManager(Ad3:引用计数)测试
/// 准备:和 Ad2 测试相同(Cube、Red 可寻址;Cube 标签 TestA,Red 标签 TestA、TestB);Play Mode Script 选 Use Asset Database
/// 规则:每次请求在发起时计数加一,每次释放减一,减到 0 才真正释放;清空不管计数
/// 结果:[PASS] 普通 log、[FAIL] 红字、[OBSERVE] 黄字(观察项),最后一行是汇总
/// 读私有字段 _infoDic 和记录类的 handle、referenceCount 用的是反射,改名的话这里也要改
/// </summary>
public class AddressablesManagerAd3Test : MonoBehaviour
{
    private const int TimeoutFrames = 300;
    private const int SettleFrames = 30;
    private const string NoThrow = "没有 throw";
    private const string LabelA = "TestA";

    private int _pass;
    private int _fail;
    private int _observe;

    private TargetManager Mgr => TargetManager.Instance;

    private IEnumerator Start()
    {
        yield return null;
        yield return CountCases();
        yield return ResetAll();
        Debug.Log($"===== Ad3 结束:PASS {_pass} / FAIL {_fail} / 观察 {_observe} =====");
    }

    private IEnumerator CountCases()
    {
        // C1 A、B 同一帧异步要 Cube,A 在加载中就释放
        yield return ResetAll();
        int countA1 = 0;
        int countB1 = 0;
        GameObject gotB1 = null;
        Mgr.LoadAsync<GameObject>("Cube", go => countA1++);
        bool loading1 = IsLoading("Cube");
        Mgr.LoadAsync<GameObject>("Cube", go =>
        {
            countB1++;
            gotB1 = go;
        });
        int countAfterTwo1 = CountOf("Cube");
        string r1 = TryRun(() => Mgr.Release<GameObject>("Cube"));
        bool stillLoading1 = IsLoading("Cube");
        int countAfterRelease1 = CountOf("Cube");
        AsyncOperationHandle handle1 = GetHandle("Cube");
        yield return WaitFor(() => countB1 > 0);
        yield return WaitFrames(5);
        if (!loading1)
        {
            Observe("C1 前提不成立", "A 发起后句柄已经完成,没造出\"加载中\"");
        }
        Check("C1 两人在加载中、A 释放:计数 2 → 1;不 throw;没有被强制完成(还在加载);记录还在;B 拿到 Cube", r1 == NoThrow && countAfterTwo1 == 2 && countAfterRelease1 == 1 && stillLoading1 && HasRecord("Cube") && handle1.IsValid() && countB1 == 1 && gotB1 != null, $"{r1};计数 {countAfterTwo1} → {countAfterRelease1};释放后还在加载:{stillLoading1};有记录:{HasRecord("Cube")};B {countB1} 次 {Describe(gotB1)}");
        Observe("C1 A 释放以后,A 的回调还执行吗", $"{countA1} 次(句柄是共用的,A 的回调摘不掉;A 释放后不该再用这个资源)");

        // C1 续:B 释放,计数归零,真正释放
        string r1b = TryRun(() => Mgr.Release<GameObject>("Cube"));
        Check("C1 续 B 释放:计数归零,记录撤掉、句柄失效", r1b == NoThrow && !HasRecord("Cube") && !handle1.IsValid(), $"{r1b};有记录:{HasRecord("Cube")};句柄有效:{handle1.IsValid()}");

        // C2 同步要两次,释放两次
        yield return ResetAll();
        Mgr.Load<GameObject>("Cube");
        Mgr.Load<GameObject>("Cube");
        int count2 = CountOf("Cube");
        AsyncOperationHandle handle2 = GetHandle("Cube");
        Mgr.Release<GameObject>("Cube");
        bool afterFirst2 = HasRecord("Cube") && handle2.IsValid() && CountOf("Cube") == 1;
        Mgr.Release<GameObject>("Cube");
        Check("C2 同步两次:计数 2;释放一次记录还在、计数 1;再释放一次记录撤掉、句柄失效", count2 == 2 && afterFirst2 && !HasRecord("Cube") && !handle2.IsValid(), $"计数 {count2};第一次释放后还在:{afterFirst2};第二次后有记录:{HasRecord("Cube")}、句柄有效:{handle2.IsValid()}");

        // C3 释放次数比加载多
        yield return ResetAll();
        Mgr.Load<GameObject>("Cube");
        Mgr.Release<GameObject>("Cube");
        string r3 = TryRun(() => Mgr.Release<GameObject>("Cube"));
        Check("C3 多释放一次:throw(记录已经没了)", r3 != NoThrow, r3);

        // C4 多条件也计数
        yield return ResetAll();
        Mgr.LoadAssets<Object>(Addressables.MergeMode.Union, LabelA);
        Mgr.LoadAssets<Object>(Addressables.MergeMode.Union, LabelA);
        string multiKey4 = MultiKey<Object>(Addressables.MergeMode.Union, LabelA);
        int count4 = CountOfKey(multiKey4);
        Mgr.ReleaseAssets<Object>(Addressables.MergeMode.Union, LabelA);
        bool afterFirst4 = HasKey(multiKey4);
        Mgr.ReleaseAssets<Object>(Addressables.MergeMode.Union, LabelA);
        Check("C4 多条件同步两次:计数 2;释放一次还在;再释放一次撤掉", count4 == 2 && afterFirst4 && !HasKey(multiKey4), $"计数 {count4};第一次释放后还在:{afterFirst4};第二次后有记录:{HasKey(multiKey4)}");

        // C5 计数归零、强制完成时,回调里又有人来要同一个资源
        yield return ResetAll();
        GameObject gotB5 = null;
        Mgr.LoadAsync<GameObject>("Cube", go =>
        {
            // A 的回调在强制完成时当场执行,这里 B 来要
            Mgr.LoadAsync<GameObject>("Cube", g => gotB5 = g);
        });
        bool loading5 = IsLoading("Cube");
        AsyncOperationHandle handle5 = GetHandle("Cube");
        string r5 = TryRun(() => Mgr.Release<GameObject>("Cube"));
        if (!loading5)
        {
            Observe("C5 前提不成立", "A 发起后句柄已经完成,没造出\"加载中\"");
        }
        Check("C5 计数 1 → 0 强制完成,回调里 B 又来要:不释放;记录还在、计数 1、句柄有效;B 拿到 Cube", r5 == NoThrow && HasRecord("Cube") && CountOf("Cube") == 1 && handle5.IsValid() && gotB5 != null, $"{r5};有记录:{HasRecord("Cube")};计数 {CountOf("Cube")};句柄有效:{handle5.IsValid()};B {Describe(gotB5)}");
        string r5b = TryRun(() => Mgr.Release<GameObject>("Cube"));
        Check("C5 续 B 释放:记录撤掉、句柄失效", r5b == NoThrow && !HasRecord("Cube") && !handle5.IsValid(), $"{r5b};有记录:{HasRecord("Cube")};句柄有效:{handle5.IsValid()}");

        // C6 清空不管计数
        yield return ResetAll();
        Mgr.Load<GameObject>("Cube");
        Mgr.Load<GameObject>("Cube");
        AsyncOperationHandle handle6 = GetHandle("Cube");
        string r6 = TryRun(() => Mgr.ReleaseAll());
        Check("C6 计数 2 时清空:0 条记录、句柄失效", r6 == NoThrow && RecordCount() == 0 && !handle6.IsValid(), $"{r6};记录 {RecordCount()} 条;句柄有效:{handle6.IsValid()}");
    }

    // ---------------- 工具 ----------------
    private IEnumerator ResetAll()
    {
        yield return WaitFrames(SettleFrames);
        TryRun(() => Mgr.ReleaseAll());
        yield return null;
    }

    private static string SingleKey(string name)
    {
        return name + "_" + nameof(GameObject);   // 和管理器单个加载的 GetKey 一致(本测试只用 GameObject)
    }

    private static string MultiKey<T>(Addressables.MergeMode mode, params string[] keys)
    {
        return string.Join("_", keys) + "_" + typeof(T).Name + "_" + mode;   // 和管理器多条件的 GetKey 一致
    }

    private IDictionary InfoDic()
    {
        FieldInfo field = typeof(TargetManager).GetField("_infoDic", BindingFlags.NonPublic | BindingFlags.Instance);
        return field?.GetValue(Mgr) as IDictionary;
    }

    private object InfoOfKey(string key)
    {
        IDictionary dic = InfoDic();
        return dic != null && dic.Contains(key) ? dic[key] : null;
    }

    private bool HasKey(string key)
    {
        return InfoOfKey(key) != null;
    }

    private int CountOfKey(string key)
    {
        object info = InfoOfKey(key);
        return info == null ? -1 : (int)info.GetType().GetField("referenceCount").GetValue(info);
    }

    private AsyncOperationHandle HandleOfKey(string key)
    {
        object info = InfoOfKey(key);
        return info == null ? default : (AsyncOperationHandle)info.GetType().GetField("handle").GetValue(info);
    }

    private bool HasRecord(string name)
    {
        return HasKey(SingleKey(name));
    }

    private int CountOf(string name)
    {
        return CountOfKey(SingleKey(name));
    }

    private AsyncOperationHandle GetHandle(string name)
    {
        return HandleOfKey(SingleKey(name));
    }

    private bool IsLoading(string name)
    {
        AsyncOperationHandle handle = GetHandle(name);
        return handle.IsValid() && !handle.IsDone;
    }

    private int RecordCount()
    {
        IDictionary dic = InfoDic();
        return dic != null ? dic.Count : -1;
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

    private void Observe(string id, string detail)
    {
        _observe++;
        Debug.LogWarning($"[OBSERVE] {id} | {detail}");
    }

    private IEnumerator WaitFrames(int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            yield return null;
        }
    }

    private IEnumerator WaitFor(Func<bool> condition)
    {
        int frames = 0;
        while (!condition() && frames < TimeoutFrames)
        {
            frames++;
            yield return null;
        }
    }

    private static string Describe(Object obj)
    {
        return obj == null ? "null" : $"{obj.name}({obj.GetType().Name})";
    }
}