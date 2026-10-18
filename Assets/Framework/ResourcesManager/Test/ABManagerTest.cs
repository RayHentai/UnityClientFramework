using System;
using System.Collections;
using System.Linq;
using UnityEngine;

/// <summary>
/// ABManager(AB1)测试,编号对应测试清单
/// 准备:
///   材质 Red → 包 mat;预设体 Cube(用 Red 材质)→ 包 model;图片 Item(Sprite)→ 包 ui
///   菜单 Tools/AB/Build (PC) 打包,StreamingAssets 里应有 PC、mat、model、ui
/// 每个用例开头先 UnloadAllBundles(false) 回到干净状态
/// 结果:[PASS] 普通 log、[FAIL] 红色,最后一行是汇总
/// 用例里出现的 Unity 红字 "Unable to open archive file" 是 L6 故意加载不存在的包,属正常
/// </summary>
public class ABManagerTest : MonoBehaviour
{
    private const int TimeoutFrames = 120;
    private const string NoThrow = "没有 throw";
    private const string DuplicateError = "another AssetBundle with the same files is already loaded";
    private const string SpriteName = "Item"; // ui 包里那张图的资源名

    private int _pass;
    private int _fail;
    private int _duplicateErrors; // Unity "同一个包重复加载" 报错次数
    private int _exceptions;      // 未捕获异常次数(协程里抛的异常不会传给调用方,只会出现在日志里)

    private ABManager Mgr => ABManager.Instance;

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
        if (type == LogType.Error && condition.Contains(DuplicateError))
            _duplicateErrors++;
        if (type == LogType.Exception)
            _exceptions++;
    }

    private IEnumerator Start()
    {
        yield return null;
        LoadCases();
        yield return LoadAsyncCases();
        UnloadCases();
        Debug.Log($"===== 结束:PASS {_pass} / FAIL {_fail} =====");
    }

    // ---------------- Load(L1 ~ L6) ----------------
    private void LoadCases()
    {
        // L1 第一次调用,目标包有依赖包
        Reset();
        GameObject cube = Mgr.Load<GameObject>("model", "Cube");
        Check("L1 第一次调用:返回预设体", cube != null, Describe(cube));
        // 主包没设过包名(打包时自动生成),它的 name 是空字符串,按 "PC" 查不到;
        // 已加载 3 个包、其中有 mat 和 model,剩下那个就是主包
        Check("L1 第一次调用:主包、依赖包、目标包都已加载", LoadedCount() == 3 && IsLoaded("mat") && IsLoaded("model"), LoadedBundles());
        Material cubeMaterial = cube != null ? cube.GetComponent<MeshRenderer>().sharedMaterial : null;
        Check("L1 第一次调用:依赖包里的材质接上了", cubeMaterial != null && cubeMaterial.name == "Red", Describe(cubeMaterial));

        // L2 依赖包已被别的包加载过,目标包未加载
        Reset();
        int duplicateBefore2 = _duplicateErrors;
        Material red = Mgr.Load<Material>("mat", "Red");
        GameObject cube2 = Mgr.Load<GameObject>("model", "Cube");
        Material cube2Material = cube2 != null ? cube2.GetComponent<MeshRenderer>().sharedMaterial : null;
        Check("L2 依赖包已加载:没有重复加载报错", _duplicateErrors == duplicateBefore2, $"重复加载报错 {_duplicateErrors - duplicateBefore2} 条");
        Check("L2 依赖包已加载:已加载的包只有 PC、mat、model 各一个", LoadedCount() == 3, LoadedBundles());
        Check("L2 依赖包已加载:预设体用的就是先取出的那个材质", red != null && ReferenceEquals(red, cube2Material), $"先取的 {Describe(red)} / 预设体上的 {Describe(cube2Material)}");

        // L3 目标包已加载,同一个包再取一次
        int duplicateBefore3 = _duplicateErrors;
        GameObject cube3 = Mgr.Load<GameObject>("model", "Cube");
        Check("L3 目标包已加载:返回同一个对象", cube2 != null && ReferenceEquals(cube2, cube3), Describe(cube3));
        Check("L3 目标包已加载:没有重复加载、包数量不变", _duplicateErrors == duplicateBefore3 && LoadedCount() == 3, LoadedBundles());

        // L4 Type 版:png 传 typeof(Sprite)
        UnityEngine.Object icon4 = Mgr.Load("ui", SpriteName, typeof(Sprite));
        Check("L4 Type 版:拿到的是 Sprite", icon4 is Sprite, $"{Describe(icon4)};ui 包里有:{BundleContents("ui")}");

        // L5 包已加载,资源名不存在
        GameObject notExist = null;
        string r5 = TryRun(() => notExist = Mgr.Load<GameObject>("model", "NotExist"));
        Check("L5 资源名不存在:返回 null、不报错", r5 == NoThrow && notExist == null, $"{r5};返回 {Describe(notExist)}");

        // L6 包名不存在,连调两次
        Reset();
        string r6First = TryRun(() => Mgr.Load<GameObject>("notexist", "X"));
        string r6Second = TryRun(() => Mgr.Load<GameObject>("notexist", "X"));
        Check("L6 包名不存在:第一次 throw 自己的报错(不是空引用)", IsOwnThrow(r6First), r6First);
        Check("L6 包名不存在:第二次照样 throw 自己的报错(没把 null 记下来)", IsOwnThrow(r6Second), r6Second);
    }

    // ---------------- LoadAsync(A1 ~ A4) ----------------
    private IEnumerator LoadAsyncCases()
    {
        // A1 未加载
        Reset();
        int count1 = 0;
        GameObject got1 = null;
        Mgr.LoadAsync<GameObject>("model", "Cube", obj =>
        {
            count1++;
            got1 = obj;
        });
        Check("A1 未加载:LoadAsync 这一行返回时回调还没执行", count1 == 0, $"次数 {count1}");
        yield return WaitFor(() => count1 > 0);
        yield return WaitFrames(5);
        Check("A1 未加载:回调执行 1 次、收到预设体", count1 == 1 && got1 != null, $"次数 {count1};收到 {Describe(got1)}");

        // A2 同一帧对同一个包 LoadAsync 两次
        Reset();
        int duplicateBefore2 = _duplicateErrors;
        int count2A = 0;
        int count2B = 0;
        Mgr.LoadAsync<GameObject>("model", "Cube", obj => count2A++);
        Mgr.LoadAsync<GameObject>("model", "Cube", obj => count2B++);
        yield return WaitFor(() => count2A > 0 && count2B > 0);
        yield return WaitFrames(5);
        Check("A2 同帧两次:两个回调各执行 1 次", count2A == 1 && count2B == 1, $"A {count2A} 次 / B {count2B} 次");
        Check("A2 同帧两次:没有重复加载报错、包只加载一次", _duplicateErrors == duplicateBefore2 && LoadedCount() == 3, $"重复加载报错 {_duplicateErrors - duplicateBefore2} 条;{LoadedBundles()}");

        // A3 回调传 null
        Reset();
        int exceptionsBefore3 = _exceptions;
        string r3 = TryRun(() => Mgr.LoadAsync<GameObject>("model", "Cube"));
        yield return WaitFrames(30);
        Check("A3 回调传 null:调用和协程里都没有报错", r3 == NoThrow && _exceptions == exceptionsBefore3, $"{r3};协程异常 {_exceptions - exceptionsBefore3} 条");

        // A4 Type 版
        Reset();
        int count4 = 0;
        UnityEngine.Object got4 = null;
        Mgr.LoadAsync("ui", SpriteName, typeof(Sprite), obj =>
        {
            count4++;
            got4 = obj;
        });
        yield return WaitFor(() => count4 > 0);
        Check("A4 Type 版:回调执行 1 次、收到 Sprite", count4 == 1 && got4 is Sprite, $"次数 {count4};收到 {Describe(got4)};ui 包里有:{BundleContents("ui")}");
    }

    // ---------------- 卸载(U1 ~ U4) ----------------
    private void UnloadCases()
    {
        // U1 UnloadBundle(false)
        Reset();
        Sprite icon1 = Mgr.Load<Sprite>("ui", SpriteName);
        string r1 = TryRun(() => Mgr.UnloadBundle("ui", false));
        Check("U1 卸包(false):不报错、ui 包已卸载", r1 == NoThrow && !IsLoaded("ui"), $"{r1};{LoadedBundles()}");
        Check("U1 卸包(false):之前取出的资源还能用", icon1 != null, Describe(icon1));
        Sprite icon1Again = Mgr.Load<Sprite>("ui", SpriteName);
        Check("U1 卸包(false):再取同一个资源,拿到的是新的一份", icon1Again != null && !ReferenceEquals(icon1, icon1Again), $"之前 {Describe(icon1)} / 再取 {Describe(icon1Again)}");

        // U2 UnloadBundle(true)
        Reset();
        Sprite icon2 = Mgr.Load<Sprite>("ui", SpriteName);
        bool gotBefore2 = icon2 != null; // 卸之前要先确实取到,否则"== null"不能说明是被卸掉的
        string r2 = TryRun(() => Mgr.UnloadBundle("ui", true));
        Check("U2 卸包(true):之前取出的资源被一起卸掉(== null)", gotBefore2 && r2 == NoThrow && icon2 == null, $"卸之前取到:{gotBefore2};{r2};资源 == null:{icon2 == null}");

        // U3 未加载的包
        Reset();
        string r3 = TryRun(() => Mgr.UnloadBundle("ui"));
        Check("U3 卸未加载的包:throw", r3 != NoThrow, r3);

        // U4 UnloadAllBundles 后再 Load
        Reset();
        Mgr.Load<GameObject>("model", "Cube");
        string r4 = TryRun(() => Mgr.UnloadAllBundles(false));
        Check("U4 全部卸载:不报错、已加载的包为 0", r4 == NoThrow && LoadedCount() == 0, $"{r4};{LoadedBundles()}");
        GameObject cube4 = null;
        string r4Again = TryRun(() => cube4 = Mgr.Load<GameObject>("model", "Cube"));
        Check("U4 全部卸载后再 Load:正常拿到", r4Again == NoThrow && cube4 != null, $"{r4Again};{Describe(cube4)}");
    }

    // ---------------- 工具 ----------------
    private void Reset()
    {
        Mgr.UnloadAllBundles(false);
    }

    private static bool IsLoaded(string bundleName)
    {
        return AssetBundle.GetAllLoadedAssetBundles().Any(bundle => bundle.name == bundleName);
    }

    private static int LoadedCount()
    {
        return AssetBundle.GetAllLoadedAssetBundles().Count();
    }

    private static string LoadedBundles()
    {
        string[] names = AssetBundle.GetAllLoadedAssetBundles()
            .Select(bundle => string.IsNullOrEmpty(bundle.name) ? "主包" : bundle.name)
            .ToArray();
        return $"已加载的包:[{string.Join(", ", names)}]";
    }

    // 列出某个已加载的包里实际有哪些资源(名字 + 类型),排查"取不到"用
    private static string BundleContents(string bundleName)
    {
        AssetBundle bundle = AssetBundle.GetAllLoadedAssetBundles().FirstOrDefault(b => b.name == bundleName);
        if (bundle == null)
            return "包未加载";
        string[] assets = bundle.LoadAllAssets().Select(asset => $"{asset.name}({asset.GetType().Name})").ToArray();
        return $"[{string.Join(", ", assets)}]";
    }

    private static bool IsOwnThrow(string result)
    {
        return result != NoThrow && !result.Contains(nameof(NullReferenceException));
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