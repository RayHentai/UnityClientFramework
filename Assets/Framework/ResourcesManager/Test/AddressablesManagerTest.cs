using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using TargetManager = AddressablesManager;   // 被测的管理器类名,改名时只改这一行

/// <summary>
/// AddressablesManager(Ad1)测试,编号对应测试清单
/// 准备:Test/Addressables 里的 Cube(预设体,用 Red 材质)、Red(材质)已标成可寻址,地址分别是 Cube、Red
/// 运行模式:Groups 窗口 Play Mode Script 先选 Use Asset Database 跑一遍;
///           再 Build → New Build → Default Build Script,切到 Use Existing Build 跑一遍
/// 结果:[PASS] 普通 log、[FAIL] 红字、[OBSERVE] 黄字(观察项,不算通过也不算失败),最后一行是汇总
/// 下面这些红字是故意制造的,属正常:
///   Addressables 自己打的 InvalidKeyException(故意加载不存在的地址、错误的类型)
///   管理器自己打的"异步加载失败"和同步 Load 的失败日志(脚本会数条数;同步日志按"在方法 Load 中"匹配)
/// 读私有字段 _infoDic 用的是反射,字段改名的话这里也要改
/// </summary>
public class AddressablesManagerTest : MonoBehaviour
{
    private const int TimeoutFrames = 300;
    private const int SettleFrames = 30;
    private const string NoThrow = "没有 throw";
    private const string MissingAddress = "NotExist";
    private const string AsyncFailLog = "异步加载失败";        // 管理器异步失败时打的日志
    private const string SyncFailLog = "在方法 Load 中";          // 管理器同步失败时打的日志(两种同步失败日志都包含这一段)

    private int _pass;
    private int _fail;
    private int _observe;
    private int _asyncFailLogs;
    private int _syncFailLogs;
    private int _exceptions;

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
            _asyncFailLogs++;
        if (type == LogType.Error && condition.Contains(SyncFailLog))
            _syncFailLogs++;
        if (type == LogType.Exception)
            _exceptions++;
    }

    private IEnumerator Start()
    {
        yield return null;
        yield return LoadCases();
        yield return LoadAsyncCases();
        yield return ReleaseCases();
        yield return InterleaveCases();
        yield return ObserveCases();
        Reset();
        Debug.Log($"===== 结束:PASS {_pass} / FAIL {_fail} / 观察 {_observe} =====");
    }

    // ---------------- 同步 Load(L1 ~ L7、D1;L2 即 S1) ----------------
    private IEnumerator LoadCases()
    {
        // L1 无记录 + D1 依赖自动加载
        Reset();
        GameObject cube1 = Mgr.Load<GameObject>("Cube");
        Check("L1 无记录:返回预设体、记进字典", cube1 != null && HasRecord<GameObject>("Cube"), $"{Describe(cube1)};有记录:{HasRecord<GameObject>("Cube")}");
        Material material1 = MaterialOf(cube1);
        Check("D1 依赖自动加载:Red 材质接上了", material1 != null && material1.name == "Red", Describe(material1));

        // L3 加载完毕再 Load
        GameObject cube3 = Mgr.Load<GameObject>("Cube");
        Check("L3 加载完毕:返回同一个对象、记录只有一条", ReferenceEquals(cube1, cube3) && RecordCount() == 1, $"{Describe(cube3)};记录 {RecordCount()} 条");

        // L2 / S1 A 异步加载中,同一帧同步 Load
        Reset();
        int countA2 = 0;
        GameObject gotA2 = null;
        Mgr.LoadAsync<GameObject>("Cube", go =>
        {
            countA2++;
            gotA2 = go;
        });
        bool loading2 = IsLoading<GameObject>("Cube");
        GameObject cube2 = Mgr.Load<GameObject>("Cube");
        Observe("X3 同步 Load 返回的那一刻,A 的回调执行了几次", $"{countA2} 次(前提:Load 之前还在加载 = {loading2})");
        yield return WaitFor(() => countA2 > 0);
        yield return WaitFrames(5);
        if (!loading2)
            Observe("L2/S1 前提不成立", "LoadAsync 返回时就已经加载完了,没测到“加载中”");
        Check("L2/S1 同步拿到资源;A 的回调 1 次;A、B 是同一个对象", cube2 != null && countA2 == 1 && ReferenceEquals(cube2, gotA2), $"同步 {Describe(cube2)};A {countA2} 次,收到 {Describe(gotA2)}");

        // L4 资源名输错
        Reset();
        int syncBefore4 = _syncFailLogs;
        GameObject got4 = null;
        string r4 = TryRun(() => got4 = Mgr.Load<GameObject>(MissingAddress));
        Check("L4 资源名输错:不 throw、返回 null、打 1 条日志、不记录", r4 == NoThrow && got4 == null && _syncFailLogs - syncBefore4 == 1 && RecordCount() == 0, $"{r4};返回 {Describe(got4)};日志 {_syncFailLogs - syncBefore4} 条;记录 {RecordCount()} 条");

        // L5 类型输错(Cube 是预设体,按 Material 要)
        Reset();
        int syncBefore5 = _syncFailLogs;
        Material got5 = null;
        string r5 = TryRun(() => got5 = Mgr.Load<Material>("Cube"));
        Check("L5 类型输错:不 throw、返回 null、打 1 条日志、不记录", r5 == NoThrow && got5 == null && _syncFailLogs - syncBefore5 == 1 && RecordCount() == 0, $"{r5};返回 {Describe(got5)};日志 {_syncFailLogs - syncBefore5} 条;记录 {RecordCount()} 条");

        // L6 卸载后再 Load
        Reset();
        GameObject before6 = Mgr.Load<GameObject>("Cube");
        Mgr.Release<GameObject>("Cube");
        GameObject after6 = Mgr.Load<GameObject>("Cube");
        Check("L6 卸载后再 Load:拿到资源、重新记录", after6 != null && HasRecord<GameObject>("Cube"), $"{Describe(after6)};有记录:{HasRecord<GameObject>("Cube")}");
        Observe("L6 卸载前后是不是同一个对象", $"{ReferenceEquals(before6, after6)}(Use Asset Database 和 Use Existing Build 可能不一样)");

        // L7 清空后再 Load
        Reset();
        Mgr.Load<GameObject>("Cube");
        Mgr.ReleaseAll();
        GameObject after7 = Mgr.Load<GameObject>("Cube");
        Check("L7 清空后再 Load:拿到资源、重新记录", after7 != null && HasRecord<GameObject>("Cube"), $"{Describe(after7)};有记录:{HasRecord<GameObject>("Cube")}");
    }

    // ---------------- 异步 LoadAsync(A1 ~ A7;A2 即 S2) ----------------
    private IEnumerator LoadAsyncCases()
    {
        // A1 无记录
        Reset();
        int count1 = 0;
        GameObject got1 = null;
        Mgr.LoadAsync<GameObject>("Cube", go =>
        {
            count1++;
            got1 = go;
        });
        Observe("A1 LoadAsync 返回的那一刻,回调执行了几次", $"{count1} 次");
        Check("A1 刚发起:已经记进字典(占位)", HasRecord<GameObject>("Cube"), $"有记录:{HasRecord<GameObject>("Cube")}");
        yield return WaitFor(() => count1 > 0);
        yield return WaitFrames(5);
        Check("A1 无记录:回调 1 次、收到预设体", count1 == 1 && got1 != null, $"{count1} 次;收到 {Describe(got1)}");

        // A2 / S2 加载中再 LoadAsync
        Reset();
        int countA = 0;
        int countB = 0;
        GameObject gotA = null;
        GameObject gotB = null;
        Mgr.LoadAsync<GameObject>("Cube", go =>
        {
            countA++;
            gotA = go;
        });
        bool loading2 = IsLoading<GameObject>("Cube");
        Mgr.LoadAsync<GameObject>("Cube", go =>
        {
            countB++;
            gotB = go;
        });
        yield return WaitFor(() => countA > 0 && countB > 0);
        yield return WaitFrames(5);
        if (!loading2)
            Observe("A2/S2 前提不成立", "第一次 LoadAsync 返回时就已经加载完了,没测到“加载中”");
        Check("A2/S2 加载中再要:A、B 回调各 1 次,同一个对象,记录 1 条", countA == 1 && countB == 1 && gotA != null && ReferenceEquals(gotA, gotB) && RecordCount() == 1, $"A {countA} 次 / B {countB} 次;A {Describe(gotA)} / B {Describe(gotB)};记录 {RecordCount()} 条");

        // A3 加载完毕再 LoadAsync:应该当场回调
        Reset();
        GameObject loaded3 = Mgr.Load<GameObject>("Cube");
        int count3 = 0;
        GameObject got3 = null;
        Mgr.LoadAsync<GameObject>("Cube", go =>
        {
            count3++;
            got3 = go;
        });
        Check("A3 加载完毕:LoadAsync 返回前回调就执行了 1 次,同一个对象", count3 == 1 && ReferenceEquals(loaded3, got3), $"返回时 {count3} 次;收到 {Describe(got3)}");

        // A4 资源名输错
        Reset();
        int asyncBefore4 = _asyncFailLogs;
        int count4 = 0;
        GameObject got4 = null;
        Mgr.LoadAsync<GameObject>(MissingAddress, go =>
        {
            count4++;
            got4 = go;
        });
        yield return WaitFor(() => count4 > 0);
        yield return WaitFrames(5);
        Check("A4 资源名输错:回调 1 次、收到 null、打 1 条日志、记录撤掉", count4 == 1 && got4 == null && _asyncFailLogs - asyncBefore4 == 1 && RecordCount() == 0, $"{count4} 次;收到 {Describe(got4)};日志 {_asyncFailLogs - asyncBefore4} 条;记录 {RecordCount()} 条");

        // A5 类型输错
        Reset();
        int asyncBefore5 = _asyncFailLogs;
        int count5 = 0;
        Material got5 = null;
        Mgr.LoadAsync<Material>("Cube", material =>
        {
            count5++;
            got5 = material;
        });
        yield return WaitFor(() => count5 > 0);
        yield return WaitFrames(5);
        Check("A5 类型输错:回调 1 次、收到 null、打 1 条日志、记录撤掉", count5 == 1 && got5 == null && _asyncFailLogs - asyncBefore5 == 1 && RecordCount() == 0, $"{count5} 次;收到 {Describe(got5)};日志 {_asyncFailLogs - asyncBefore5} 条;记录 {RecordCount()} 条");

        // A6 卸载后再 LoadAsync
        Reset();
        Mgr.Load<GameObject>("Cube");
        Mgr.Release<GameObject>("Cube");
        int count6 = 0;
        GameObject got6 = null;
        Mgr.LoadAsync<GameObject>("Cube", go =>
        {
            count6++;
            got6 = go;
        });
        yield return WaitFor(() => count6 > 0);
        Check("A6 卸载后再 LoadAsync:回调 1 次、收到预设体", count6 == 1 && got6 != null, $"{count6} 次;收到 {Describe(got6)}");

        // A7 清空后再 LoadAsync
        Reset();
        Mgr.Load<GameObject>("Cube");
        Mgr.ReleaseAll();
        int count7 = 0;
        GameObject got7 = null;
        Mgr.LoadAsync<GameObject>("Cube", go =>
        {
            count7++;
            got7 = go;
        });
        yield return WaitFor(() => count7 > 0);
        Check("A7 清空后再 LoadAsync:回调 1 次、收到预设体", count7 == 1 && got7 != null, $"{count7} 次;收到 {Describe(got7)}");
    }

    // ---------------- 卸载(R1 ~ R7;R2 即 S3,R6 即 S5) ----------------
    private IEnumerator ReleaseCases()
    {
        // R1 无记录
        Reset();
        string r1 = TryRun(() => Mgr.Release<GameObject>("Cube"));
        Check("R1 无记录:throw", r1 != NoThrow, r1);

        // R2 / S3 加载中 Release
        Reset();
        int count2 = 0;
        GameObject got2 = null;
        Mgr.LoadAsync<GameObject>("Cube", go =>
        {
            count2++;
            got2 = go;
        });
        bool loading2 = IsLoading<GameObject>("Cube");
        AsyncOperationHandle handle2 = GetHandle<GameObject>("Cube");
        string r2 = TryRun(() => Mgr.Release<GameObject>("Cube"));
        Observe("R2/S3 Release 返回的那一刻,A 的回调执行了几次", $"{count2} 次");
        yield return WaitFrames(SettleFrames);
        if (!loading2)
            Observe("R2/S3 前提不成立", "LoadAsync 返回时就已经加载完了,没测到“加载中”");
        Check("R2/S3 加载中 Release:不 throw、记录撤掉、句柄失效", r2 == NoThrow && !HasRecord<GameObject>("Cube") && !handle2.IsValid(), $"{r2};有记录:{HasRecord<GameObject>("Cube")};句柄有效:{handle2.IsValid()}");
        Check("R2/S3 A 的回调 1 次、拿到了资源", count2 == 1 && !ReferenceEquals(got2, null), $"{count2} 次;收到的是不是 null:{ReferenceEquals(got2, null)}");

        // R3 加载完毕 Release
        Reset();
        Mgr.Load<GameObject>("Cube");
        AsyncOperationHandle handle3 = GetHandle<GameObject>("Cube");
        string r3 = TryRun(() => Mgr.Release<GameObject>("Cube"));
        Check("R3 加载完毕 Release:不 throw、记录撤掉、句柄失效", r3 == NoThrow && !HasRecord<GameObject>("Cube") && !handle3.IsValid(), $"{r3};有记录:{HasRecord<GameObject>("Cube")};句柄有效:{handle3.IsValid()}");

        // R4 资源名输错
        Reset();
        string r4 = TryRun(() => Mgr.Release<GameObject>(MissingAddress));
        Check("R4 资源名输错:throw(字典里没有这条)", r4 != NoThrow, r4);

        // R5 类型输错(Cube 按 GameObject 加载过,按 Material 释放)
        Reset();
        Mgr.Load<GameObject>("Cube");
        string r5 = TryRun(() => Mgr.Release<Material>("Cube"));
        Check("R5 类型输错:throw,Cube 的记录还在", r5 != NoThrow && HasRecord<GameObject>("Cube"), $"{r5};Cube 有记录:{HasRecord<GameObject>("Cube")}");

        // R6 / S5 一条已加载完、一条还在加载,ReleaseAll
        Reset();
        Mgr.Load<Material>("Red");
        int count6 = 0;
        GameObject got6 = null;
        Mgr.LoadAsync<GameObject>("Cube", go =>
        {
            count6++;
            got6 = go;
        });
        bool loading6 = IsLoading<GameObject>("Cube");
        AsyncOperationHandle redHandle6 = GetHandle<Material>("Red");
        AsyncOperationHandle cubeHandle6 = GetHandle<GameObject>("Cube");
        string r6 = TryRun(() => Mgr.ReleaseAll());
        Observe("R6/S5 ReleaseAll 返回的那一刻,Cube 的回调执行了几次", $"{count6} 次");
        yield return WaitFrames(SettleFrames);
        if (!loading6)
            Observe("R6/S5 前提不成立", "LoadAsync 返回时 Cube 就已经加载完了,没测到“加载中”");
        Check("R6/S5 ReleaseAll:不 throw、记录清空、两个句柄都失效", r6 == NoThrow && RecordCount() == 0 && !redHandle6.IsValid() && !cubeHandle6.IsValid(), $"{r6};记录 {RecordCount()} 条;Red 句柄有效:{redHandle6.IsValid()};Cube 句柄有效:{cubeHandle6.IsValid()}");
        Check("R6/S5 加载中的 Cube:回调 1 次、拿到了资源", count6 == 1 && !ReferenceEquals(got6, null), $"{count6} 次;收到的是不是 null:{ReferenceEquals(got6, null)}");

        // R7 全部加载完毕,ReleaseAll
        Reset();
        Mgr.Load<GameObject>("Cube");
        Mgr.Load<Material>("Red");
        AsyncOperationHandle cubeHandle7 = GetHandle<GameObject>("Cube");
        AsyncOperationHandle redHandle7 = GetHandle<Material>("Red");
        string r7 = TryRun(() => Mgr.ReleaseAll());
        Check("R7 全部加载完毕 ReleaseAll:不 throw、记录清空、两个句柄都失效", r7 == NoThrow && RecordCount() == 0 && !cubeHandle7.IsValid() && !redHandle7.IsValid(), $"{r7};记录 {RecordCount()} 条;Cube 句柄有效:{cubeHandle7.IsValid()};Red 句柄有效:{redHandle7.IsValid()}");
    }

    // ---------------- 交错场景(S4、S6、S7、S8;S1 见 L2,S2 见 A2,S3 见 R2,S5 见 R6) ----------------
    private IEnumerator InterleaveCases()
    {
        // S4 A、B 同一帧异步要一个不存在的地址
        Reset();
        int asyncBefore4 = _asyncFailLogs;
        int countA4 = 0;
        int countB4 = 0;
        GameObject gotA4 = null;
        GameObject gotB4 = null;
        Mgr.LoadAsync<GameObject>(MissingAddress, go =>
        {
            countA4++;
            gotA4 = go;
        });
        bool loading4 = IsLoading<GameObject>(MissingAddress);
        Mgr.LoadAsync<GameObject>(MissingAddress, go =>
        {
            countB4++;
            gotB4 = go;
        });
        yield return WaitFor(() => countA4 > 0 && countB4 > 0);
        yield return WaitFrames(5);
        if (!loading4)
            Observe("S4 前提不成立", "第一次 LoadAsync 返回时就已经结束了,B 走的不是“加载中”");
        Check("S4 A、B 回调各 1 次、都收到 null;日志只打 1 条;记录撤掉", countA4 == 1 && countB4 == 1 && gotA4 == null && gotB4 == null && _asyncFailLogs - asyncBefore4 == 1 && RecordCount() == 0, $"A {countA4} 次 / B {countB4} 次;A {Describe(gotA4)} / B {Describe(gotB4)};日志 {_asyncFailLogs - asyncBefore4} 条;记录 {RecordCount()} 条");

        // S6 S4 的基础上,A 的回调里重试同一个地址;B 的回调执行时,重试的记录应该还在
        Reset();
        int countB6 = 0;
        int countRetry6 = 0;
        bool recordWhenB6 = false;
        Mgr.LoadAsync<GameObject>(MissingAddress, go =>
        {
            Mgr.LoadAsync<GameObject>(MissingAddress, retry => countRetry6++);   // A 的回调里重试
        });
        bool loading6 = IsLoading<GameObject>(MissingAddress);
        Mgr.LoadAsync<GameObject>(MissingAddress, go =>
        {
            countB6++;
            recordWhenB6 = HasRecord<GameObject>(MissingAddress);   // B 的回调执行时,重试的记录在不在
        });
        yield return WaitFor(() => countB6 > 0 && countRetry6 > 0);
        yield return WaitFrames(5);
        if (!loading6)
            Observe("S6 前提不成立", "第一次 LoadAsync 返回时就已经结束了,B 走的不是“加载中”");
        Check("S6 B 的回调执行时,A 重试新建的记录还在(B 没有按 key 删掉它)", countB6 == 1 && recordWhenB6, $"B {countB6} 次;B 回调时有记录:{recordWhenB6}");
        Check("S6 重试最后也失败:重试的回调 1 次,记录最终撤掉", countRetry6 == 1 && RecordCount() == 0, $"重试回调 {countRetry6} 次;记录 {RecordCount()} 条");

        // S7 A 异步加载 Cube 中,ReleaseAll;A 的回调里加载 Red,清空以后 Red 的记录应该还在
        Reset();
        int countRed7 = 0;
        Material gotRed7 = null;
        Mgr.LoadAsync<GameObject>("Cube", go =>
        {
            Mgr.LoadAsync<Material>("Red", material =>
            {
                countRed7++;
                gotRed7 = material;
            });
        });
        bool loading7 = IsLoading<GameObject>("Cube");
        Mgr.ReleaseAll();
        bool redRecordAfterClear7 = HasRecord<Material>("Red");
        Observe("S7 ReleaseAll 返回的那一刻,Red 有没有记录", $"{redRecordAfterClear7}(false 说明 A 的回调没有在强制完成时当场执行)");
        yield return WaitFor(() => countRed7 > 0);
        yield return WaitFrames(5);
        if (!loading7)
            Observe("S7 前提不成立", "LoadAsync 返回时 Cube 就已经加载完了,没测到“加载中”");
        Check("S7 Red 回调 1 次、收到材质,Red 的记录还在,之后能正常 Release", countRed7 == 1 && gotRed7 != null && HasRecord<Material>("Red") && TryRun(() => Mgr.Release<Material>("Red")) == NoThrow, $"Red {countRed7} 次;收到 {Describe(gotRed7)}");

        // S8 A 异步加载一个不存在的地址,同一帧同步 Load 它
        Reset();
        int asyncBefore8 = _asyncFailLogs;
        int syncBefore8 = _syncFailLogs;
        int countA8 = 0;
        GameObject gotA8 = null;
        Mgr.LoadAsync<GameObject>(MissingAddress, go =>
        {
            countA8++;
            gotA8 = go;
        });
        bool loading8 = IsLoading<GameObject>(MissingAddress);
        GameObject gotB8 = null;
        string r8 = TryRun(() => gotB8 = Mgr.Load<GameObject>(MissingAddress));
        yield return WaitFor(() => countA8 > 0);
        yield return WaitFrames(5);
        if (!loading8)
            Observe("S8 前提不成立", "LoadAsync 返回时就已经结束了,同步走的不是“加载中”");
        Check("S8 同步:不 throw、返回 null、打 1 条同步日志", r8 == NoThrow && gotB8 == null && _syncFailLogs - syncBefore8 == 1, $"{r8};返回 {Describe(gotB8)};同步日志 {_syncFailLogs - syncBefore8} 条");
        Check("S8 A:回调 1 次、收到 null、打 1 条异步日志;记录撤掉", countA8 == 1 && gotA8 == null && _asyncFailLogs - asyncBefore8 == 1 && RecordCount() == 0, $"A {countA8} 次;收到 {Describe(gotA8)};异步日志 {_asyncFailLogs - asyncBefore8} 条;记录 {RecordCount()} 条");
    }

    // ---------------- 观察项(X1、X2、X4;X3 在 L2 里) ----------------
    private IEnumerator ObserveCases()
    {
        // X1 失败的句柄,回调执行完以后还有没有效
        Reset();
        int count1 = 0;
        Mgr.LoadAsync<GameObject>(MissingAddress, go => count1++);
        AsyncOperationHandle failed1 = GetHandle<GameObject>(MissingAddress);
        yield return WaitFor(() => count1 > 0);
        yield return WaitFrames(5);
        Observe("X1 失败的句柄,没人 Release 时还有没有效", $"IsValid = {failed1.IsValid()}(true 说明它还占着,失败的句柄也应该 Release)");

        // X2 绕过管理器,对还在加载的句柄直接 Addressables.Release
        Reset();
        int exceptionsBefore2 = _exceptions;
        int count2 = 0;
        AsyncOperationHandle<GameObject> raw2 = Addressables.LoadAssetAsync<GameObject>("Cube");
        bool doneBefore2 = raw2.IsDone;
        raw2.Completed += h => count2++;
        string r2 = TryRun(() => Addressables.Release(raw2));
        yield return WaitFrames(SettleFrames);
        Observe("X2 加载中直接 Release", $"Release 前已完成:{doneBefore2};Release:{r2};回调 {count2} 次;句柄有效:{raw2.IsValid()};新增异常 {_exceptions - exceptionsBefore2} 条");

        // X4 失败句柄 IsDone 变成 true 的那一刻,字典里的记录还在不在
        Reset();
        Mgr.LoadAsync<GameObject>(MissingAddress, go => { });
        AsyncOperationHandle failed4 = GetHandle<GameObject>(MissingAddress);
        string seen4 = failed4.IsDone ? $"LoadAsync 返回时就已结束;有记录:{HasRecord<GameObject>(MissingAddress)}" : null;
        int frames4 = 0;
        while (seen4 == null && frames4 < TimeoutFrames)
        {
            yield return null;
            frames4++;
            if (failed4.IsValid() && failed4.IsDone)
                seen4 = $"第 {frames4} 帧看到 IsDone;有记录:{HasRecord<GameObject>(MissingAddress)}";
            else if (!HasRecord<GameObject>(MissingAddress))
                seen4 = $"第 {frames4} 帧记录已经没了(转接头已执行)";
        }
        Observe("X4 失败句柄结束的那一刻,记录还在不在", seen4 ?? "超时");
    }

    // ---------------- 工具 ----------------
    private void Reset()
    {
        TryRun(() => Mgr.ReleaseAll());
    }

    private static string KeyOf<T>(string name)
    {
        return name + "_" + typeof(T).Name;   // 和管理器的 GetKey 一致
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

    private bool HasRecord<T>(string name)
    {
        IDictionary dic = InfoDic();
        return dic != null && dic.Contains(KeyOf<T>(name));
    }

    private int RecordCount()
    {
        IDictionary dic = InfoDic();
        return dic != null ? dic.Count : -1;
    }

    private AsyncOperationHandle GetHandle<T>(string name)
    {
        IDictionary dic = InfoDic();
        return dic != null && dic.Contains(KeyOf<T>(name)) ? HandleOf(dic[KeyOf<T>(name)]) : default;
    }

    private bool IsLoading<T>(string name)
    {
        AsyncOperationHandle handle = GetHandle<T>(name);
        return handle.IsValid() && !handle.IsDone;
    }

    private static Material MaterialOf(GameObject go)
    {
        if (go == null)
            return null;
        MeshRenderer meshRenderer = go.GetComponent<MeshRenderer>();
        return meshRenderer != null ? meshRenderer.sharedMaterial : null;
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
            yield return null;
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

    private static string Describe(UnityEngine.Object obj)
    {
        return obj == null ? "null" : $"{obj.name}({obj.GetType().Name})";
    }
}