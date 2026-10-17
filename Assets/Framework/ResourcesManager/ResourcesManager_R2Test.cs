using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// ResourcesManager_v1(R2)测试
/// 资源都放在 Assets/Resources/ResTest/ 下:
///   Item        导入成 Sprite 的 png
///   Item 1~14   选中 Item 按 Ctrl+D 复制 14 次得到(每个用例用自己的资源,互不影响)
///   Cube        预设体
///   1           音频
/// 结果:[PASS] 普通 log、[FAIL] 红色,最后一行是汇总
/// U5、U5b 用 Application.logMessageReceived 统计 Unity 的 "UnloadAsset may only be used..." 报错
/// </summary>
public class ResourcesManager_v1Test : MonoBehaviour
{
    private const string Dir = "ResTest/";
    private const int TimeoutFrames = 60;
    private const string NoThrow = "没有 throw";

    private const string UnityUnloadError = "UnloadAsset may only be used";

    private int _pass;
    private int _fail;
    private int _unityUnloadErrors; // Unity "UnloadAsset 不能用在 GameObject/Component 上" 那条报错出现的次数

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
        yield return LoadCases();
        yield return LoadAsyncCases();
        yield return UnloadCases();
        yield return TypeCases();
        Debug.Log($"===== 结束:PASS {_pass} / FAIL {_fail} =====");
    }

    // ---------------- Load ----------------
    private IEnumerator LoadCases()
    {
        // L1 无记录
        Sprite s1 = Mgr.Load<Sprite>(ItemPath(1));
        Check("L1 Load·无记录:返回资源", s1 != null, Describe(s1));

        // L2 加载中:先 LoadAsync 攒一个回调,同一帧同步 Load
        int countA = 0;
        Mgr.LoadAsync<Sprite>(ItemPath(2), s => countA++);
        Sprite s2 = Mgr.Load<Sprite>(ItemPath(2));
        Check("L2 Load·加载中:返回资源", s2 != null, Describe(s2));
        Check("L2 Load·加载中:攒着的回调在 Load 返回前执行 1 次", countA == 1, $"次数 {countA}");
        yield return WaitFrames(10);
        Check("L2 Load·加载中:10 帧后回调仍是 1 次(协程已停)", countA == 1, $"次数 {countA}");

        // L3 已加载
        Sprite s3a = Mgr.Load<Sprite>(ItemPath(3));
        Sprite s3b = Mgr.Load<Sprite>(ItemPath(3));
        Check("L3 Load·已加载:返回同一个对象", s3a != null && ReferenceEquals(s3a, s3b), Describe(s3b));

        // L4 路径不存在:连 Load 两次(第一次无记录,第二次有记录但资源为 null)
        Sprite a4 = null;
        Sprite b4 = null;
        string r4 = TryRun(() =>
        {
            a4 = Mgr.Load<Sprite>(Dir + "NotExist_L4");
            b4 = Mgr.Load<Sprite>(Dir + "NotExist_L4");
        });
        Check("L4 Load·路径不存在:两次都返回 null、不报错", r4 == NoThrow && a4 == null && b4 == null,
            $"{r4};第一次 {Describe(a4)};第二次 {Describe(b4)}");

        // L5 类型对不上:泛型版先占了 key,再用 Type 版访问同一路径
        Mgr.Load<Sprite>(ItemPath(4));
        ExpectThrow("L5 Load·类型对不上:throw", () => Mgr.Load(ItemPath(4), typeof(Sprite)));
    }

    // ---------------- LoadAsync ----------------
    private IEnumerator LoadAsyncCases()
    {
        // A1 无记录
        int count1 = 0;
        int frame1 = -1;
        Sprite got1 = null;
        int callFrame1 = Time.frameCount;
        Mgr.LoadAsync<Sprite>(ItemPath(5), s =>
        {
            count1++;
            frame1 = Time.frameCount;
            got1 = s;
        });
        yield return WaitFor(() => count1 > 0);
        yield return WaitFrames(3);
        Check("A1 LoadAsync·无记录:回调执行 1 次", count1 == 1, $"次数 {count1}");
        Check("A1 LoadAsync·无记录:回调在之后的帧", frame1 > callFrame1, $"调用帧 {callFrame1} / 回调帧 {frame1}");
        Check("A1 LoadAsync·无记录:收到资源", got1 != null, Describe(got1));

        // A2 加载中:同一帧两次 LoadAsync
        int count2A = 0;
        int count2B = 0;
        int frame2A = -1;
        int frame2B = -1;
        Mgr.LoadAsync<Sprite>(ItemPath(6), s =>
        {
            count2A++;
            frame2A = Time.frameCount;
        });
        Mgr.LoadAsync<Sprite>(ItemPath(6), s =>
        {
            count2B++;
            frame2B = Time.frameCount;
        });
        yield return WaitFor(() => count2A > 0 && count2B > 0);
        yield return WaitFrames(3);
        Check("A2 LoadAsync·加载中:两个回调各执行 1 次", count2A == 1 && count2B == 1, $"A {count2A} 次 / B {count2B} 次");
        Check("A2 LoadAsync·加载中:两个回调在同一帧", frame2A == frame2B && frame2A > 0, $"A 帧 {frame2A} / B 帧 {frame2B}");

        // A3 已加载:回调应在 LoadAsync 这一行返回前执行
        Sprite s7 = Mgr.Load<Sprite>(ItemPath(7));
        int count3 = 0;
        Sprite got3 = null;
        Mgr.LoadAsync<Sprite>(ItemPath(7), s =>
        {
            count3++;
            got3 = s;
        });
        Check("A3 LoadAsync·已加载:回调当场执行 1 次", count3 == 1, $"次数 {count3}");
        Check("A3 LoadAsync·已加载:收到同一个资源", s7 != null && ReferenceEquals(s7, got3), Describe(got3));

        // A4 路径不存在:记录在、资源为 null,回调应当场执行并收到 null,而不是一直攒着
        Mgr.Load<Sprite>(Dir + "NotExist_A4");
        int count4 = 0;
        bool gotNull4 = false;
        Mgr.LoadAsync<Sprite>(Dir + "NotExist_A4", s =>
        {
            count4++;
            gotNull4 = s == null;
        });
        Check("A4 LoadAsync·路径不存在:回调当场执行 1 次、收到 null", count4 == 1 && gotNull4, $"次数 {count4};收到 null:{gotNull4}");

        // A5 类型对不上
        Mgr.Load<Sprite>(ItemPath(8));
        ExpectThrow("A5 LoadAsync·类型对不上:throw", () => Mgr.LoadAsync(ItemPath(8), typeof(Sprite), o => { }));

        // A6 回调传 null:无记录、已加载各一次
        string r6a = TryRun(() => Mgr.LoadAsync<Sprite>(ItemPath(9)));
        yield return WaitFrames(10);
        string r6b = TryRun(() => Mgr.LoadAsync<Sprite>(ItemPath(9)));
        Check("A6 LoadAsync·回调传 null:不报错", r6a == NoThrow && r6b == NoThrow, $"无记录:{r6a};已加载:{r6b}");
    }

    // ---------------- UnloadAsset ----------------
    private IEnumerator UnloadCases()
    {
        // U1 无记录
        ExpectThrow("U1 UnloadAsset·无记录:throw", () => Mgr.UnloadAsset<Sprite>(Dir + "NotExist_U1"));

        // U2 加载中,撤掉 cbA
        // 撤回调必须传同一个委托实例,所以先存进变量;现场再写一个 lambda 撤不掉
        int countU2A = 0;
        int countU2B = 0;
        UnityAction<Sprite> cbA = s => countU2A++;
        UnityAction<Sprite> cbB = s => countU2B++;
        Mgr.LoadAsync(ItemPath(10), cbA);
        Mgr.LoadAsync(ItemPath(10), cbB);
        Mgr.UnloadAsset(ItemPath(10), cbA);
        yield return WaitFor(() => countU2B > 0);
        yield return WaitFrames(3);
        Check("U2 UnloadAsset·加载中传 cbA:cbA 不执行、cbB 执行 1 次", countU2A == 0 && countU2B == 1, $"A {countU2A} 次 / B {countU2B} 次");
        Sprite s10 = Mgr.Load<Sprite>(ItemPath(10));
        Check("U2 UnloadAsset·加载中传 cbA:记录还在,已加载", s10 != null, Describe(s10));

        // U3 加载中,不传回调
        int countU3 = 0;
        Mgr.LoadAsync<Sprite>(ItemPath(11), s => countU3++);
        Mgr.UnloadAsset<Sprite>(ItemPath(11));
        yield return WaitFor(() => countU3 > 0);
        yield return WaitFrames(3);
        Check("U3 UnloadAsset·加载中不传回调:攒着的回调照常执行 1 次", countU3 == 1, $"次数 {countU3}");

        // U4 已加载(音频):卸掉、记录移除,再 Load 能重新拿到
        AudioClip clip = Mgr.Load<AudioClip>(Dir + "1");
        string r4 = TryRun(() => Mgr.UnloadAsset<AudioClip>(Dir + "1"));
        AudioClip clip2 = Mgr.Load<AudioClip>(Dir + "1");
        Check("U4 UnloadAsset·已加载(音频):卸载不报错,再 Load 能拿到", clip != null && r4 == NoThrow && clip2 != null,
            $"卸载:{r4};再 Load:{Describe(clip2)}");

        // U5 已加载(预设体):不调 Resources.UnloadAsset、不出 Unity 报错,记录移除(第二次卸载 throw "不存在")
        GameObject prefab = Mgr.Load<GameObject>(Dir + "Cube");
        int errorsBefore5 = _unityUnloadErrors;
        string r5First = TryRun(() => Mgr.UnloadAsset<GameObject>(Dir + "Cube"));
        string r5Second = TryRun(() => Mgr.UnloadAsset<GameObject>(Dir + "Cube"));
        int errors5 = _unityUnloadErrors - errorsBefore5;
        Check("U5 UnloadAsset·已加载(预设体):不出 Unity 报错、记录移除",
            prefab != null && r5First == NoThrow && errors5 == 0 && r5Second != NoThrow,
            $"第一次:{r5First};Unity 报错 {errors5} 条;第二次:{r5Second}");

        // U5b 已加载(预设体上的组件):Load<Transform> 拿到的是预设体根上的 Transform,同样不能传给 Resources.UnloadAsset
        Transform prefabTransform = Mgr.Load<Transform>(Dir + "Cube");
        int errorsBefore5b = _unityUnloadErrors;
        string r5bFirst = TryRun(() => Mgr.UnloadAsset<Transform>(Dir + "Cube"));
        string r5bSecond = TryRun(() => Mgr.UnloadAsset<Transform>(Dir + "Cube"));
        int errors5b = _unityUnloadErrors - errorsBefore5b;
        Check("U5b UnloadAsset·已加载(组件):不出 Unity 报错、记录移除",
            prefabTransform != null && r5bFirst == NoThrow && errors5b == 0 && r5bSecond != NoThrow,
            $"Load 拿到:{Describe(prefabTransform)};第一次:{r5bFirst};Unity 报错 {errors5b} 条;第二次:{r5bSecond}");

        // U6 路径不存在:记录在、资源为 null,卸载不报错、记录移除
        Mgr.Load<Sprite>(Dir + "NotExist_U6");
        string r6First = TryRun(() => Mgr.UnloadAsset<Sprite>(Dir + "NotExist_U6"));
        string r6Second = TryRun(() => Mgr.UnloadAsset<Sprite>(Dir + "NotExist_U6"));
        Check("U6 UnloadAsset·路径不存在:不报错、记录移除", r6First == NoThrow && r6Second != NoThrow,
            $"第一次:{r6First};第二次:{r6Second}");

        // U7 类型对不上
        Mgr.Load<Sprite>(Dir + "Item");
        ExpectThrow("U7 UnloadAsset·类型对不上:throw", () => Mgr.UnloadAsset(Dir + "Item", typeof(Sprite)));
    }

    // ---------------- Type 版 ----------------
    private IEnumerator TypeCases()
    {
        // T1 Load(Type) 无记录:png 传 typeof(Sprite),拿到的是 Sprite
        UnityEngine.Object o1 = Mgr.Load(ItemPath(12), typeof(Sprite));
        Check("T1 Load(Type)·无记录:拿到 Sprite", o1 is Sprite, Describe(o1));

        // T2 LoadAsync(Type) 加载中:两个回调各执行 1 次,收到 Sprite
        int countT2A = 0;
        int countT2B = 0;
        bool isSpriteA = false;
        bool isSpriteB = false;
        Mgr.LoadAsync(ItemPath(13), typeof(Sprite), o =>
        {
            countT2A++;
            isSpriteA = o is Sprite;
        });
        Mgr.LoadAsync(ItemPath(13), typeof(Sprite), o =>
        {
            countT2B++;
            isSpriteB = o is Sprite;
        });
        yield return WaitFor(() => countT2A > 0 && countT2B > 0);
        yield return WaitFrames(3);
        Check("T2 LoadAsync(Type)·加载中:两个回调各 1 次且收到 Sprite", countT2A == 1 && countT2B == 1 && isSpriteA && isSpriteB,
            $"A {countT2A} 次 / B {countT2B} 次;A 是 Sprite:{isSpriteA};B 是 Sprite:{isSpriteB}");

        // T3 UnloadAsset(Type) 已加载(图片)
        UnityEngine.Object o3 = Mgr.Load(ItemPath(14), typeof(Texture2D));
        string r3 = TryRun(() => Mgr.UnloadAsset(ItemPath(14), typeof(Texture2D)));
        UnityEngine.Object o3Again = Mgr.Load(ItemPath(14), typeof(Texture2D));
        Check("T3 UnloadAsset(Type)·已加载:卸载不报错,再 Load 能拿到", o3 != null && r3 == NoThrow && o3Again != null,
            $"卸载:{r3};再 Load:{Describe(o3Again)}");
    }

    // ---------------- 工具 ----------------
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

    private void Observe(string id, string detail)
    {
        Debug.LogWarning($"[OBS] {id} | {detail}");
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