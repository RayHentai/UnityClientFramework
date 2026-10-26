using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using TargetManager = AddressablesManager;   // 被测的管理器类名,改名时只改这一行
using Object = UnityEngine.Object;

/// <summary>
/// AddressablesManager(Ad2:多条件加载)测试
/// 准备:
///   1. Groups 窗口 → Tools → Window → Labels(或 Inspector 上的标签下拉框 → Manage Labels),加两个标签 TestA、TestB
///   2. Cube(预设体)勾 TestA;Red(材质)勾 TestA、TestB
///   3. Play Mode Script 选 Use Asset Database
/// 预期的资源:TestA = {Cube, Red};TestB = {Red};TestA ∩ TestB = {Red}
/// 结果:[PASS] 普通 log、[FAIL] 红字、[OBSERVE] 黄字(观察项),最后一行是汇总
/// 下面这些红字是故意制造的,属正常:Addressables 自己的 InvalidKeyException、管理器的"异步加载失败"和 LoadAssets 的失败日志
/// 读私有字段 _infoDic 用的是反射,字段改名或 key 拼法改了,这里也要改
/// </summary>
public class AddressablesManagerAd2Test : MonoBehaviour
{
    private const int TimeoutFrames = 300;
    private const int SettleFrames = 30;
    private const string NoThrow = "没有 throw";
    private const string LabelA = "TestA";
    private const string LabelB = "TestB";
    private const string Missing = "NotExistLabel";
    private const string AsyncFailLog = "异步加载失败";
    private const string SyncFailLog = "在方法 LoadAssets 中";

    private int _pass;
    private int _fail;
    private int _observe;
    private int _asyncFailLogs;
    private int _syncFailLogs;
    private int _nullRefs;

    private TargetManager Mgr => TargetManager.Instance;

    private void OnEnable()
    {
        Application.logMessageReceived += OnLog;
    }

    private void OnDisable()
    {
        Application.logMessageReceived -= OnLog;
    }

    private void OnLog(string condition, string stackTrace, LogType type)
    {
        if (type == LogType.Error && condition.Contains(AsyncFailLog))
        {
            _asyncFailLogs++;
        }
        if (type == LogType.Error && condition.Contains(SyncFailLog))
        {
            _syncFailLogs++;
        }
        if (type == LogType.Exception && condition.Contains("NullReferenceException"))
        {
            _nullRefs++;
        }
    }

    private IEnumerator Start()
    {
        yield return null;
        yield return SyncCases();
        yield return AsyncCases();
        yield return KeyCases();
        yield return ReleaseCases();
        yield return ObserveCases();
        yield return ResetAll();
        Debug.Log($"===== Ad2 结束:PASS {_pass} / FAIL {_fail} / 观察 {_observe} =====");
    }

    // ---------------- 同步 LoadAssets(M1 ~ M5) ----------------
    private IEnumerator SyncCases()
    {
        // M1 无记录 · 成功
        yield return ResetAll();
        IList<Object> list1 = Mgr.LoadAssets<Object>(Addressables.MergeMode.Union, LabelA);
        Check("M1 无记录:返回 2 个资源、记进字典", list1 != null && list1.Count == 2 && HasRecord<Object>(Addressables.MergeMode.Union, LabelA), $"{DescribeList(list1)};有记录:{HasRecord<Object>(Addressables.MergeMode.Union, LabelA)}");

        // M3 已完成 · 成功:返回的是副本
        IList<Object> list3 = Mgr.LoadAssets<Object>(Addressables.MergeMode.Union, LabelA);
        bool differentInstance = !ReferenceEquals(list1, list3);
        list3?.Clear();
        IList<Object> list3b = Mgr.LoadAssets<Object>(Addressables.MergeMode.Union, LabelA);
        Check("M3 已完成:返回 2 个资源;每次是新的列表,清空它不影响下一次", differentInstance && list3b != null && list3b.Count == 2 && RecordCount() == 1, $"新列表:{differentInstance};清空后再取:{DescribeList(list3b)};记录 {RecordCount()} 条");

        // M2 加载中 · 成功:先异步发起,同一帧同步要
        yield return ResetAll();
        int itemsA2 = 0;
        int completeA2 = 0;
        Mgr.LoadAssetsAsync<Object>(Addressables.MergeMode.Union, o => itemsA2++, l => completeA2++, LabelA);
        bool loading2 = IsLoading<Object>(Addressables.MergeMode.Union, LabelA);
        IList<Object> list2 = Mgr.LoadAssets<Object>(Addressables.MergeMode.Union, LabelA);
        int completeAtReturn2 = completeA2;
        int itemsAtReturn2 = itemsA2;
        yield return WaitFrames(SettleFrames);
        if (!loading2)
        {
            Observe("M2 前提不成立", "发起后句柄已经完成,没造出\"加载中\"");
        }
        Check("M2 加载中:同步拿到 2 个资源;A 的整批回调在同步返回前执行 1 次;A 的逐个回调最终正好 2 次(不是 4 次)", list2 != null && list2.Count == 2 && completeAtReturn2 == 1 && completeA2 == 1 && itemsA2 == 2, $"{DescribeList(list2)};A 整批:返回时 {completeAtReturn2} 次,最终 {completeA2} 次;A 逐个:返回时 {itemsAtReturn2} 次,最终 {itemsA2} 次");

        // M4 无记录 · 失败
        yield return ResetAll();
        int syncLogs4 = _syncFailLogs;
        IList<Object> got4 = null;
        string r4 = TryRun(() => got4 = Mgr.LoadAssets<Object>(Addressables.MergeMode.Union, Missing));
        Check("M4 无记录 · 失败:不 throw、返回 null、不留记录、同步日志 1 条", r4 == NoThrow && got4 == null && !HasRecord<Object>(Addressables.MergeMode.Union, Missing) && _syncFailLogs - syncLogs4 == 1, $"{r4};返回 {DescribeList(got4)};有记录:{HasRecord<Object>(Addressables.MergeMode.Union, Missing)};同步日志 {_syncFailLogs - syncLogs4} 条");

        // M5 X4 窗口:异步要一个不存在的,同一帧同步也要
        yield return ResetAll();
        int syncLogs5 = _syncFailLogs;
        int asyncLogs5 = _asyncFailLogs;
        int nullRefs5 = _nullRefs;
        int complete5 = 0;
        bool gotNull5 = false;
        Mgr.LoadAssetsAsync<Object>(Addressables.MergeMode.Union, null, l =>
        {
            complete5++;
            gotNull5 = l == null;
        }, Missing);
        IList<Object> got5 = null;
        string r5 = TryRun(() => got5 = Mgr.LoadAssets<Object>(Addressables.MergeMode.Union, Missing));
        yield return WaitFrames(SettleFrames);
        Check("M5 X4 窗口:同步返回 null、不 throw;A 的整批回调 1 次收到 null;记录撤掉;同步日志 1 条、异步日志 1 条;没有空引用", r5 == NoThrow && got5 == null && complete5 == 1 && gotNull5 && !HasRecord<Object>(Addressables.MergeMode.Union, Missing) && _syncFailLogs - syncLogs5 == 1 && _asyncFailLogs - asyncLogs5 == 1 && _nullRefs == nullRefs5, $"{r5};返回 {DescribeList(got5)};A 整批 {complete5} 次,null:{gotNull5};有记录:{HasRecord<Object>(Addressables.MergeMode.Union, Missing)};同步日志 {_syncFailLogs - syncLogs5}、异步日志 {_asyncFailLogs - asyncLogs5};空引用 {_nullRefs - nullRefs5}");
    }

    // ---------------- 异步 LoadAssetsAsync(N1 ~ N6) ----------------
    private IEnumerator AsyncCases()
    {
        // N1 无记录 · 成功:逐个回调正好 2 次
        yield return ResetAll();
        int items1 = 0;
        int complete1 = 0;
        IList<Object> got1 = null;
        Mgr.LoadAssetsAsync<Object>(Addressables.MergeMode.Union, o => items1++, l =>
        {
            complete1++;
            got1 = l;
        }, LabelA);
        bool recordAtStart1 = HasRecord<Object>(Addressables.MergeMode.Union, LabelA);
        int completeAtReturn1 = complete1;
        yield return WaitFor(() => complete1 > 0);
        yield return WaitFrames(SettleFrames);
        Check("N1 无记录:一发起就记录;逐个回调 2 次;整批回调 1 次、在返回之后、收到 2 个", recordAtStart1 && items1 == 2 && complete1 == 1 && completeAtReturn1 == 0 && got1 != null && got1.Count == 2, $"发起时有记录:{recordAtStart1};逐个 {items1} 次;整批 {complete1} 次(返回时 {completeAtReturn1});收到 {DescribeList(got1)}");

        // N2 加载中:A、B 同一帧
        yield return ResetAll();
        int itemsA2 = 0;
        int itemsB2 = 0;
        int completeA2 = 0;
        int completeB2 = 0;
        Mgr.LoadAssetsAsync<Object>(Addressables.MergeMode.Union, o => itemsA2++, l => completeA2++, LabelA);
        bool loading2 = IsLoading<Object>(Addressables.MergeMode.Union, LabelA);
        Mgr.LoadAssetsAsync<Object>(Addressables.MergeMode.Union, o => itemsB2++, l => completeB2++, LabelA);
        yield return WaitFor(() => completeA2 > 0 && completeB2 > 0);
        yield return WaitFrames(SettleFrames);
        if (!loading2)
        {
            Observe("N2 前提不成立", "A 发起后句柄已经完成,没造出\"加载中\"");
        }
        Check("N2 加载中:A、B 逐个各 2 次、整批各 1 次;只有 1 条记录", itemsA2 == 2 && itemsB2 == 2 && completeA2 == 1 && completeB2 == 1 && RecordCount() == 1, $"A 逐个 {itemsA2}、整批 {completeA2};B 逐个 {itemsB2}、整批 {completeB2};记录 {RecordCount()} 条");

        // N3 已完成:C 来要,当场执行
        int itemsC3 = 0;
        int completeC3 = 0;
        Mgr.LoadAssetsAsync<Object>(Addressables.MergeMode.Union, o => itemsC3++, l => completeC3++, LabelA);
        Check("N3 已完成:逐个 2 次、整批 1 次,都在返回之前", itemsC3 == 2 && completeC3 == 1, $"返回时逐个 {itemsC3}、整批 {completeC3}");

        // N6 交出去的是副本:A 在整批回调里清空列表,之后 B 来要
        yield return ResetAll();
        bool aDone6 = false;
        Mgr.LoadAssetsAsync<Object>(Addressables.MergeMode.Union, null, l =>
        {
            l?.Clear();
            aDone6 = true;
        }, LabelA);
        yield return WaitFor(() => aDone6);
        IList<Object> gotB6 = null;
        int itemsB6 = 0;
        Mgr.LoadAssetsAsync<Object>(Addressables.MergeMode.Union, o => itemsB6++, l => gotB6 = l, LabelA);
        Check("N6 A 清空了自己的列表,B 仍然收到 2 个、逐个 2 次", gotB6 != null && gotB6.Count == 2 && itemsB6 == 2, $"B 收到 {DescribeList(gotB6)};逐个 {itemsB6} 次");

        // N4 无记录 · 失败
        yield return ResetAll();
        int asyncLogs4 = _asyncFailLogs;
        int items4 = 0;
        int complete4 = 0;
        bool gotNull4 = false;
        Mgr.LoadAssetsAsync<Object>(Addressables.MergeMode.Union, o => items4++, l =>
        {
            complete4++;
            gotNull4 = l == null;
        }, Missing);
        yield return WaitFor(() => complete4 > 0);
        yield return WaitFrames(SettleFrames);
        Check("N4 失败:逐个 0 次;整批 1 次收到 null;记录撤掉;异步日志 1 条", items4 == 0 && complete4 == 1 && gotNull4 && !HasRecord<Object>(Addressables.MergeMode.Union, Missing) && _asyncFailLogs - asyncLogs4 == 1, $"逐个 {items4};整批 {complete4} 次,null:{gotNull4};有记录:{HasRecord<Object>(Addressables.MergeMode.Union, Missing)};异步日志 {_asyncFailLogs - asyncLogs4}");

        // N5 X4 窗口:A、B 同一帧要不存在的
        yield return ResetAll();
        int asyncLogs5 = _asyncFailLogs;
        int nullRefs5 = _nullRefs;
        int completeA5 = 0;
        int completeB5 = 0;
        bool nullA5 = false;
        bool nullB5 = false;
        string r5 = NoThrow;
        Mgr.LoadAssetsAsync<Object>(Addressables.MergeMode.Union, null, l =>
        {
            completeA5++;
            nullA5 = l == null;
        }, Missing);
        bool doneAtStart5 = GetHandle<Object>(Addressables.MergeMode.Union, Missing).IsDone;
        r5 = TryRun(() => Mgr.LoadAssetsAsync<Object>(Addressables.MergeMode.Union, null, l =>
        {
            completeB5++;
            nullB5 = l == null;
        }, Missing));
        yield return WaitFrames(SettleFrames);
        if (!doneAtStart5)
        {
            Observe("N5 前提不成立", "A 发起后句柄还没结束,没造出 X4 窗口");
        }
        Check("N5 X4 窗口:B 不 throw;A、B 整批各 1 次收到 null;异步日志只有 1 条;没有空引用", r5 == NoThrow && completeA5 == 1 && completeB5 == 1 && nullA5 && nullB5 && _asyncFailLogs - asyncLogs5 == 1 && _nullRefs == nullRefs5, $"{r5};A {completeA5} 次 null:{nullA5};B {completeB5} 次 null:{nullB5};异步日志 {_asyncFailLogs - asyncLogs5};空引用 {_nullRefs - nullRefs5}");
    }

    // ---------------- key(K1 ~ K3) ----------------
    private IEnumerator KeyCases()
    {
        // K1 先单个、再多条件,条件同名
        yield return ResetAll();
        GameObject single1 = Mgr.Load<GameObject>("Cube");
        IList<GameObject> multi1 = null;
        string r1 = TryRun(() => multi1 = Mgr.LoadAssets<GameObject>(Addressables.MergeMode.Union, "Cube"));
        Check("K1 先单个 Load(\"Cube\")、再多条件 \"Cube\":不 throw,多条件拿到 1 个,同一个对象,2 条记录", r1 == NoThrow && multi1 != null && multi1.Count == 1 && multi1[0] == single1 && RecordCount() == 2, $"{r1};多条件 {DescribeList(multi1)};记录 {RecordCount()} 条");

        // K2 反过来
        yield return ResetAll();
        Mgr.LoadAssets<GameObject>(Addressables.MergeMode.Union, "Cube");
        GameObject single2 = Mgr.Load<GameObject>("Cube");
        Check("K2 先多条件、再单个:单个拿到预设体(不是静默 null),2 条记录", single2 != null && RecordCount() == 2, $"单个 {Describe(single2)};记录 {RecordCount()} 条");

        // K3 同条件、不同合并模式
        yield return ResetAll();
        IList<Object> union3 = Mgr.LoadAssets<Object>(Addressables.MergeMode.Union, LabelA, LabelB);
        IList<Object> inter3 = Mgr.LoadAssets<Object>(Addressables.MergeMode.Intersection, LabelA, LabelB);
        Check("K3 同条件不同模式:并集 2 个、交集 1 个(Red),2 条记录", union3 != null && union3.Count == 2 && inter3 != null && inter3.Count == 1 && inter3[0].name == "Red" && RecordCount() == 2, $"并集 {DescribeList(union3)};交集 {DescribeList(inter3)};记录 {RecordCount()} 条");
    }

    // ---------------- 释放(R1 ~ R5) ----------------
    private IEnumerator ReleaseCases()
    {
        // R1 没记录
        yield return ResetAll();
        string r1 = TryRun(() => Mgr.ReleaseAssets<Object>(Addressables.MergeMode.Union, LabelA));
        Check("R1 没记录:throw", r1 != NoThrow, r1);

        // R2 加载中
        yield return ResetAll();
        int complete2 = 0;
        Mgr.LoadAssetsAsync<Object>(Addressables.MergeMode.Union, null, l => complete2++, LabelA);
        bool loading2 = IsLoading<Object>(Addressables.MergeMode.Union, LabelA);
        AsyncOperationHandle handle2 = GetHandle<Object>(Addressables.MergeMode.Union, LabelA);
        string r2 = TryRun(() => Mgr.ReleaseAssets<Object>(Addressables.MergeMode.Union, LabelA));
        int completeAtReturn2 = complete2;
        yield return WaitFrames(SettleFrames);
        if (!loading2)
        {
            Observe("R2 前提不成立", "发起后句柄已经完成,没造出\"加载中\"");
        }
        Check("R2 加载中:不 throw;A 的整批回调在释放返回前执行 1 次;记录撤掉;句柄失效", r2 == NoThrow && completeAtReturn2 == 1 && complete2 == 1 && !HasRecord<Object>(Addressables.MergeMode.Union, LabelA) && !handle2.IsValid(), $"{r2};整批:返回时 {completeAtReturn2},最终 {complete2};有记录:{HasRecord<Object>(Addressables.MergeMode.Union, LabelA)};句柄有效:{handle2.IsValid()}");

        // R3 已完成
        yield return ResetAll();
        Mgr.LoadAssets<Object>(Addressables.MergeMode.Union, LabelA);
        AsyncOperationHandle handle3 = GetHandle<Object>(Addressables.MergeMode.Union, LabelA);
        string r3 = TryRun(() => Mgr.ReleaseAssets<Object>(Addressables.MergeMode.Union, LabelA));
        Check("R3 已完成:不 throw、记录撤掉、句柄失效", r3 == NoThrow && !HasRecord<Object>(Addressables.MergeMode.Union, LabelA) && !handle3.IsValid(), $"{r3};有记录:{HasRecord<Object>(Addressables.MergeMode.Union, LabelA)};句柄有效:{handle3.IsValid()}");

        // R4 单个的 Release 不碰多条件的记录
        yield return ResetAll();
        Mgr.Load<GameObject>("Cube");
        Mgr.LoadAssets<GameObject>(Addressables.MergeMode.Union, "Cube");
        AsyncOperationHandle multiHandle4 = GetHandle<GameObject>(Addressables.MergeMode.Union, "Cube");
        Mgr.Release<GameObject>("Cube");
        Check("R4 单个 Release(\"Cube\"):多条件 \"Cube\" 的记录还在、句柄有效", HasRecord<GameObject>(Addressables.MergeMode.Union, "Cube") && multiHandle4.IsValid(), $"多条件有记录:{HasRecord<GameObject>(Addressables.MergeMode.Union, "Cube")};句柄有效:{multiHandle4.IsValid()}");

        // R5 清空:一条已完成、一条加载中
        yield return ResetAll();
        Mgr.LoadAssets<Object>(Addressables.MergeMode.Union, LabelA);
        int complete5 = 0;
        Mgr.LoadAssetsAsync<Object>(Addressables.MergeMode.Intersection, null, l => complete5++, LabelA, LabelB);
        bool loading5 = IsLoading<Object>(Addressables.MergeMode.Intersection, LabelA, LabelB);
        AsyncOperationHandle doneHandle5 = GetHandle<Object>(Addressables.MergeMode.Union, LabelA);
        AsyncOperationHandle loadingHandle5 = GetHandle<Object>(Addressables.MergeMode.Intersection, LabelA, LabelB);
        string r5 = TryRun(() => Mgr.ReleaseAll());
        yield return WaitFrames(SettleFrames);
        if (!loading5)
        {
            Observe("R5 前提不成立", "交集那条发起后已经完成,没造出\"加载中\"");
        }
        Check("R5 清空:不 throw;0 条记录;两个句柄都失效;加载中那条的整批回调 1 次", r5 == NoThrow && RecordCount() == 0 && !doneHandle5.IsValid() && !loadingHandle5.IsValid() && complete5 == 1, $"{r5};记录 {RecordCount()} 条;句柄有效:{doneHandle5.IsValid()} / {loadingHandle5.IsValid()};整批 {complete5} 次");
    }

    // ---------------- 观察项 ----------------
    private IEnumerator ObserveCases()
    {
        // X1 按类型过滤:TestA 里有预设体和材质,按 Material 要
        yield return ResetAll();
        IList<Material> materials1 = Mgr.LoadAssets<Material>(Addressables.MergeMode.Union, LabelA);
        Observe("X1 LoadAssets<Material>(TestA)", $"收到 {DescribeList(materials1)}(预计只有 Red)");

        // X2 逐个回调和整批回调的先后
        yield return ResetAll();
        List<string> order2 = new List<string>();
        Mgr.LoadAssetsAsync<Object>(Addressables.MergeMode.Union, o => order2.Add("逐个"), l => order2.Add("整批"), LabelA);
        yield return WaitFor(() => order2.Contains("整批"));
        Observe("X2 回调顺序", string.Join(" → ", order2));

        // X3 多条件加载不存在的标签:句柄一交回来是不是就已经结束(多条件版的 X4)
        yield return ResetAll();
        Mgr.LoadAssetsAsync<Object>(Addressables.MergeMode.Union, null, null, Missing);
        AsyncOperationHandle handle3 = GetHandle<Object>(Addressables.MergeMode.Union, Missing);
        Observe("X3 不存在的标签:发起那一刻", $"IsDone:{handle3.IsDone};有记录:{HasRecord<Object>(Addressables.MergeMode.Union, Missing)}");
        yield return WaitFrames(SettleFrames);
    }

    // ---------------- 工具 ----------------
    private IEnumerator ResetAll()
    {
        // 先等一会儿,让还没执行的转接头执行完,再清空(否则清空会释放掉还没撤记录的失败句柄,转接头随后读到失效的句柄;已知问题,见 README)
        yield return WaitFrames(SettleFrames);
        TryRun(() => Mgr.ReleaseAll());
        yield return null;
    }

    private static string KeyOf<T>(Addressables.MergeMode mode, params string[] keys)
    {
        return string.Join("_", keys) + "_" + typeof(T).Name + "_" + mode;   // 和管理器的多条件 GetKey 一致
    }

    private IDictionary InfoDic()
    {
        FieldInfo field = typeof(TargetManager).GetField("_infoDic", BindingFlags.NonPublic | BindingFlags.Instance);
        return field?.GetValue(Mgr) as IDictionary;
    }

    // 记录类是管理器里的私有类,用反射读它的 handle、referenceCount 字段
    private static AsyncOperationHandle HandleOf(object info)
    {
        return info == null ? default : (AsyncOperationHandle)info.GetType().GetField("handle").GetValue(info);
    }

    private static int CountOf(object info)
    {
        return info == null ? -1 : (int)info.GetType().GetField("referenceCount").GetValue(info);
    }

    private bool HasRecord<T>(Addressables.MergeMode mode, params string[] keys)
    {
        IDictionary dic = InfoDic();
        return dic != null && dic.Contains(KeyOf<T>(mode, keys));
    }

    private int RecordCount()
    {
        IDictionary dic = InfoDic();
        return dic != null ? dic.Count : -1;
    }

    private AsyncOperationHandle GetHandle<T>(Addressables.MergeMode mode, params string[] keys)
    {
        IDictionary dic = InfoDic();
        return dic != null && dic.Contains(KeyOf<T>(mode, keys)) ? HandleOf(dic[KeyOf<T>(mode, keys)]) : default;
    }

    private bool IsLoading<T>(Addressables.MergeMode mode, params string[] keys)
    {
        AsyncOperationHandle handle = GetHandle<T>(mode, keys);
        return handle.IsValid() && !handle.IsDone;
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

    private static string DescribeList<T>(IList<T> list) where T : Object
    {
        if (list == null)
        {
            return "null";
        }
        List<string> names = new List<string>();
        foreach (T item in list)
        {
            names.Add(Describe(item));
        }
        return $"{list.Count} 个 [{string.Join(", ", names)}]";
    }
}