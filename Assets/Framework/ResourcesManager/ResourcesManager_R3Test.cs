using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// ResourcesManager_v2(R3 引用计数)测试,编号对应测试清单
/// 资源都放在 Assets/Resources/ResTest/ 下:
///   Item 1 ~ Item 17  导入成 Sprite 的 png(R2 测试已有 Item 1~14,再按 Ctrl+D 复制出 Item 15~17)
///   Cube              预设体
/// 结果:[PASS] 普通 log、[FAIL] 红色,最后一行是汇总
/// R12 用 Application.logMessageReceived 统计 Unity 的 "UnloadAsset may only be used..." 报错
/// </summary>
public class ResourcesManager_R3Test : MonoBehaviour
{
    private const string Dir = "ResTest/";
    private const int TimeoutFrames = 60;
    private const string NoThrow = "没有 throw";
    private const string UnityUnloadError = "UnloadAsset may only be used";

    private int _pass;
    private int _fail;
    private int _unityUnloadErrors;

    private ResourcesManager Mgr => ResourcesManager.Instance;

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
        if (type == LogType.Error && condition.Contains(UnityUnloadError))
            _unityUnloadErrors++;
    }

    private IEnumerator Start()
    {
        yield return null;
        yield return AddCases();
        yield return ReleaseCases();
        yield return QueryCases();
        yield return CompleteCases();
        yield return UnloadUnusedCases();
        Debug.Log($"===== 结束:PASS {_pass} / FAIL {_fail} =====");
    }

    // ---------------- 加载:计数 +1(R1 ~ R6) ----------------
    private IEnumerator AddCases()
    {
        // R1 无记录 · Load
        Mgr.Load<Sprite>(ItemPath(1));
        CheckCount<Sprite>("R1 无记录·Load:计数 1", ItemPath(1), 1);

        // R2 无记录 · LoadAsync:调用之后计数马上是 1
        int count2 = 0;
        Mgr.LoadAsync<Sprite>(ItemPath(2), s => count2++);
        CheckCount<Sprite>("R2 无记录·LoadAsync:计数 1", ItemPath(2), 1);
        yield return WaitFor(() => count2 > 0);

        // R3 加载中 · Load
        int count3 = 0;
        Mgr.LoadAsync<Sprite>(ItemPath(3), s => count3++);
        Mgr.Load<Sprite>(ItemPath(3));
        CheckCount<Sprite>("R3 加载中·Load:计数 2", ItemPath(3), 2);
        Check("R3 加载中·Load:攒着的回调当场执行 1 次", count3 == 1, $"次数 {count3}");

        // R4 加载中 · LoadAsync
        int count4 = 0;
        Mgr.LoadAsync<Sprite>(ItemPath(4), s => count4++);
        Mgr.LoadAsync<Sprite>(ItemPath(4), s => count4++);
        CheckCount<Sprite>("R4 加载中·LoadAsync:计数 2", ItemPath(4), 2);
        yield return WaitFor(() => count4 >= 2);

        // R5 已加载 · Load
        Mgr.Load<Sprite>(ItemPath(5));
        Mgr.Load<Sprite>(ItemPath(5));
        CheckCount<Sprite>("R5 已加载·Load:计数 2", ItemPath(5), 2);

        // R6 已加载 · LoadAsync
        Mgr.Load<Sprite>(ItemPath(6));
        int count6 = 0;
        Mgr.LoadAsync<Sprite>(ItemPath(6), s => count6++);
        Check("R6 已加载·LoadAsync:回调当场执行 1 次", count6 == 1, $"次数 {count6}");
        CheckCount<Sprite>("R6 已加载·LoadAsync:计数 2", ItemPath(6), 2);
    }

    // ---------------- 卸载:计数 -1(R7 ~ R13) ----------------
    private IEnumerator ReleaseCases()
    {
        // R7 无记录 · UnloadAsset
        ExpectThrow("R7 无记录·UnloadAsset:throw", () => Mgr.UnloadAsset<Sprite>(Dir + "NotExist_R7"));

        // R8 加载中 · UnloadAsset:撤掉自己的回调,别人的照常
        // 撤回调必须传同一个委托实例,所以先存进变量
        int count8A = 0;
        int count8B = 0;
        UnityAction<Sprite> cb8A = s => count8A++;
        UnityAction<Sprite> cb8B = s => count8B++;
        Mgr.LoadAsync(ItemPath(7), cb8A);
        Mgr.LoadAsync(ItemPath(7), cb8B);
        Mgr.UnloadAsset(ItemPath(7), false, cb8A);
        CheckCount<Sprite>("R8 加载中·UnloadAsset:计数 2→1", ItemPath(7), 1);
        yield return WaitFor(() => count8B > 0);
        yield return WaitFrames(3);
        Check("R8 加载中·UnloadAsset:自己的回调不执行、别人的执行 1 次", count8A == 0 && count8B == 1, $"A {count8A} 次 / B {count8B} 次");

        // R9 已加载 · UnloadAsset,减完不为 0(即使要求立即卸也不卸)
        Mgr.Load<Sprite>(ItemPath(8));
        Mgr.Load<Sprite>(ItemPath(8));
        string r9 = TryRun(() => Mgr.UnloadAsset<Sprite>(ItemPath(8), true));
        Check("R9 已加载·减完不为 0:不报错", r9 == NoThrow, r9);
        CheckCount<Sprite>("R9 已加载·减完不为 0:只 -1,记录还在(计数 1)", ItemPath(8), 1);

        // R10 已加载 · UnloadAsset,减到 0、立即卸(用 Texture2D,确保能被 Resources.UnloadAsset 卸)
        Texture2D t9 = Mgr.Load<Texture2D>(ItemPath(9));
        string r10 = TryRun(() => Mgr.UnloadAsset<Texture2D>(ItemPath(9), true));
        string c10 = CountOf<Texture2D>(ItemPath(9));
        Check("R10 已加载·减到 0、立即卸:记录被删", t9 != null && r10 == NoThrow && IsThrow(c10), $"卸载:{r10};再查计数:{c10}");
        Texture2D t9Again = Mgr.Load<Texture2D>(ItemPath(9));
        Check("R10 已加载·减到 0、立即卸:再 Load 能重新拿到", t9Again != null, Describe(t9Again));
        CheckCount<Texture2D>("R10 已加载·减到 0、立即卸:再 Load 后计数 1", ItemPath(9), 1);

        // R11 已加载 · UnloadAsset,减到 0、不立即卸
        Sprite s10 = Mgr.Load<Sprite>(ItemPath(10));
        Mgr.UnloadAsset<Sprite>(ItemPath(10), false);
        CheckCount<Sprite>("R11 已加载·减到 0、不立即卸:记录还在,计数 0", ItemPath(10), 0);
        Sprite s10Again = Mgr.Load<Sprite>(ItemPath(10));
        Check("R11 已加载·减到 0、不立即卸:再 Load 拿到同一个对象", s10 != null && ReferenceEquals(s10, s10Again), Describe(s10Again));
        CheckCount<Sprite>("R11 已加载·减到 0、不立即卸:再 Load 后计数 1", ItemPath(10), 1);

        // R12 已加载(预设体) · UnloadAsset,减到 0、立即卸
        Mgr.Load<GameObject>(Dir + "Cube");
        int errorsBefore12 = _unityUnloadErrors;
        string r12 = TryRun(() => Mgr.UnloadAsset<GameObject>(Dir + "Cube", true));
        int errors12 = _unityUnloadErrors - errorsBefore12;
        string c12 = CountOf<GameObject>(Dir + "Cube");
        Check("R12 预设体·立即卸:不出 Unity 报错、记录被删", r12 == NoThrow && errors12 == 0 && IsThrow(c12),
            $"卸载:{r12};Unity 报错 {errors12} 条;再查计数:{c12}");

        // R13 卸载比加载多一次
        Mgr.Load<Sprite>(ItemPath(11));
        Mgr.UnloadAsset<Sprite>(ItemPath(11), false);
        ExpectThrow("R13 卸载比加载多一次:throw", () => Mgr.UnloadAsset<Sprite>(ItemPath(11), false));
    }

    // ---------------- 查询(R14 ~ R15) ----------------
    private IEnumerator QueryCases()
    {
        // R14 无记录 · GetReferenceCount
        ExpectThrow("R14 无记录·GetReferenceCount:throw", () => Mgr.GetReferenceCount<Sprite>(Dir + "NotExist_R14"));

        // R15 加载中 / 已加载 · GetReferenceCount
        int count15 = 0;
        Mgr.LoadAsync<Sprite>(ItemPath(12), s => count15++);
        CheckCount<Sprite>("R15 加载中·GetReferenceCount:1", ItemPath(12), 1);
        yield return WaitFor(() => count15 > 0);
        CheckCount<Sprite>("R15 已加载·GetReferenceCount:1", ItemPath(12), 1);
    }

    // ---------------- 协程加载完成(C1 ~ C3) ----------------
    private IEnumerator CompleteCases()
    {
        // C1 加载期间全部卸载、立即卸:加载完后卸资源、删记录,回调不执行
        int count1 = 0;
        UnityAction<Texture2D> cb1 = t => count1++;
        Mgr.LoadAsync(ItemPath(13), cb1);
        Mgr.UnloadAsset(ItemPath(13), true, cb1);
        yield return WaitFor(() => IsThrow(CountOf<Texture2D>(ItemPath(13))));
        string c1 = CountOf<Texture2D>(ItemPath(13));
        Check("C1 加载期间全卸、立即卸:加载完记录被删、回调不执行", count1 == 0 && IsThrow(c1), $"回调 {count1} 次;再查计数:{c1}");

        // C2 加载期间全部卸载、不立即卸:加载完记下资源,不卸不删,计数 0
        int count2 = 0;
        UnityAction<Sprite> cb2 = s => count2++;
        Mgr.LoadAsync(ItemPath(14), cb2);
        Mgr.UnloadAsset(ItemPath(14), false, cb2);
        yield return WaitFrames(30);
        string c2 = CountOf<Sprite>(ItemPath(14));
        Check("C2 加载期间全卸、不立即卸:记录还在、计数 0、回调不执行", count2 == 0 && c2 == "0", $"回调 {count2} 次;计数:{c2}");
        // 已加载的记录:再 LoadAsync,回调应当场执行(证明资源已记下、不是还在加载)
        int countAfter = 0;
        Mgr.LoadAsync<Sprite>(ItemPath(14), s => countAfter++);
        Check("C2 加载期间全卸、不立即卸:资源已记下(再 LoadAsync 回调当场执行)", countAfter == 1, $"次数 {countAfter}");

        // C3 加载完成时计数不为 0:执行攒着的回调,计数不变
        int count3A = 0;
        int count3B = 0;
        Mgr.LoadAsync<Sprite>(ItemPath(15), s => count3A++);
        Mgr.LoadAsync<Sprite>(ItemPath(15), s => count3B++);
        yield return WaitFor(() => count3A > 0 && count3B > 0);
        yield return WaitFrames(3);
        Check("C3 加载完成·计数不为 0:两个回调各执行 1 次", count3A == 1 && count3B == 1, $"A {count3A} 次 / B {count3B} 次");
        CheckCount<Sprite>("C3 加载完成·计数不为 0:计数不变(2)", ItemPath(15), 2);
    }

    // ---------------- UnloadUnusedAssets(U1) ----------------
    private IEnumerator UnloadUnusedCases()
    {
        Mgr.Load<Sprite>(ItemPath(16));
        Mgr.UnloadAsset<Sprite>(ItemPath(16), false); // 计数 0,记录留着
        Mgr.Load<Sprite>(ItemPath(17));               // 计数 1
        bool done = false;
        Mgr.UnloadUnusedAssets(() => done = true);
        yield return WaitFor(() => done);
        string c16 = CountOf<Sprite>(ItemPath(16));
        string c17 = CountOf<Sprite>(ItemPath(17));
        Check("U1 UnloadUnusedAssets:回调执行", done, $"回调执行:{done}");
        Check("U1 UnloadUnusedAssets:计数为 0 的记录被删", IsThrow(c16), $"再查计数:{c16}");
        Check("U1 UnloadUnusedAssets:计数不为 0 的保留、计数不变", c17 == "1", $"计数:{c17}");
    }

    // ---------------- 工具 ----------------
    private string CountOf<T>(string path)
    {
        try
        {
            return Mgr.GetReferenceCount<T>(path).ToString();
        }
        catch (Exception e)
        {
            return $"throw:{e.Message}";
        }
    }

    private static bool IsThrow(string result)
    {
        return result.StartsWith("throw");
    }

    private void CheckCount<T>(string id, string path, int expected)
    {
        string count = CountOf<T>(path);
        Check(id, count == expected.ToString(), $"计数 {count}");
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
            return $"throw:{e.Message}";
        }
    }

    private void ExpectThrow(string id, Action action)
    {
        string result = TryRun(action);
        Check(id, result != NoThrow, result);
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

    private static string ItemPath(int index)
    {
        return $"{Dir}Item {index}";
    }

    private static string Describe(UnityEngine.Object obj)
    {
        return obj == null ? "null" : $"{obj.name}({obj.GetType().Name})";
    }
}