using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEngine;

/// <summary>
/// ABManager(AB2)测试,编号对应测试清单
/// 准备:
///   材质 Red → 包 mat;预设体 Cube(用 Red 材质)→ 包 model;图片 Item(Sprite)→ 包 ui
///   菜单 Tools/AB/Build (PC) 打包,StreamingAssets 里应有 PC、mat、model、ui
/// 每个用例开头先 Reset()(UnloadAllBundles(false))回到干净状态
/// 结果:[PASS] 普通 log、[FAIL] 红字、[OBSERVE] 黄字(观察项,不算通过也不算失败),最后一行是汇总
/// 下面这些红字是故意制造的,属正常:
///   "Unable to open archive file"(故意加载不存在的包)
///   S3 / S4 / S6 里协程抛出的异常(脚本会数条数)
/// 读私有字段 _bundleDic、_runningCoroutines 用的是反射,字段改名的话这里也要改
/// </summary>
public class ABManagerTest : MonoBehaviour
{
    private const int TimeoutFrames = 120;
    private const int SettleFrames = 30;
    private const string NoThrow = "没有 throw";
    private const string DuplicateError = "another AssetBundle with the same files is already loaded";
    private const string SpriteName = "Item"; // ui 包里那张图的资源名
    private const string MissingBundle = "notexist";

    private int _pass;
    private int _fail;
    private int _observe;
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
        yield return InterleaveCases();
        yield return DependencyCases();
        yield return ObserveCases();
        Reset();
        Debug.Log($"===== 结束:PASS {_pass} / FAIL {_fail} / 观察 {_observe} =====");
    }

    // ---------------- 同步 Load(L1 ~ L6,沿用 AB1) ----------------
    private void LoadCases()
    {
        // L1 第一次调用,目标包有依赖包
        Reset();
        GameObject cube = Mgr.Load<GameObject>("model", "Cube");
        Check("L1 第一次调用:返回预设体", cube != null, Describe(cube));
        // 主包没设过包名,name 是空字符串;已加载 3 个包、其中有 mat 和 model,剩下那个就是主包
        Check("L1 第一次调用:主包、依赖包、目标包都已加载", LoadedCount() == 3 && IsLoaded("mat") && IsLoaded("model"), LoadedBundles());
        Material cubeMaterial = MaterialOf(cube);
        Check("L1 第一次调用:依赖包里的材质接上了", cubeMaterial != null && cubeMaterial.name == "Red", Describe(cubeMaterial));

        // L2 依赖包已被别的包加载过,目标包未加载
        Reset();
        int duplicateBefore2 = _duplicateErrors;
        Material red = Mgr.Load<Material>("mat", "Red");
        GameObject cube2 = Mgr.Load<GameObject>("model", "Cube");
        Material cube2Material = MaterialOf(cube2);
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

        // L6 包文件不存在,连调两次
        Reset();
        string r6First = TryRun(() => Mgr.Load<GameObject>(MissingBundle, "X"));
        string r6Second = TryRun(() => Mgr.Load<GameObject>(MissingBundle, "X"));
        Check("L6 包不存在:第一次 throw 自己的报错(不是空引用)", IsOwnThrow(r6First), r6First);
        Check("L6 包不存在:第二次照样 throw 自己的报错(没留下记录)", IsOwnThrow(r6Second) && !HasRecord(MissingBundle), $"{r6Second};有记录:{HasRecord(MissingBundle)}");
    }

    // ---------------- 异步 LoadAsync(A1 ~ A5,A2 即 S2) ----------------
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
        Check("A1 未加载:回调执行 1 次、收到预设体,材质接上了", count1 == 1 && got1 != null && MaterialOf(got1) != null, $"次数 {count1};收到 {Describe(got1)};材质 {Describe(MaterialOf(got1))}");

        // A2 / S2 同一帧对同一个包 LoadAsync 两次(A 在加载,C 在等)
        Reset();
        int duplicateBefore2 = _duplicateErrors;
        int count2A = 0;
        int count2C = 0;
        Mgr.LoadAsync<GameObject>("model", "Cube", obj => count2A++);
        Mgr.LoadAsync<GameObject>("model", "Cube", obj => count2C++);
        yield return WaitFor(() => count2A > 0 && count2C > 0);
        yield return WaitFrames(5);
        Check("A2/S2 同帧两次:两个回调各执行 1 次", count2A == 1 && count2C == 1, $"A {count2A} 次 / C {count2C} 次");
        Check("A2/S2 同帧两次:没有重复加载报错、包只加载一次", _duplicateErrors == duplicateBefore2 && LoadedCount() == 3, $"重复加载报错 {_duplicateErrors - duplicateBefore2} 条;{LoadedBundles()}");
        Check("H 请求都完成后:协程句柄集合为空", RunningCount() == 0, $"集合里还有 {RunningCount()} 个(-1 表示没找到字段)");

        // A3 回调传 null
        Reset();
        int exceptionsBefore3 = _exceptions;
        string r3 = TryRun(() => Mgr.LoadAsync<GameObject>("model", "Cube"));
        yield return WaitFrames(SettleFrames);
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
        Check("A4 Type 版:回调执行 1 次、收到 Sprite", count4 == 1 && got4 is Sprite, $"次数 {count4};收到 {Describe(got4)}");

        // A5 资源名不存在(异步)
        Reset();
        int count5 = 0;
        GameObject got5 = null;
        int exceptionsBefore5 = _exceptions;
        Mgr.LoadAsync<GameObject>("model", "NotExist", obj =>
        {
            count5++;
            got5 = obj;
        });
        yield return WaitFor(() => count5 > 0);
        Check("A5 资源名不存在:回调执行 1 次、收到 null、不报错", count5 == 1 && got5 == null && _exceptions == exceptionsBefore5, $"次数 {count5};收到 {Describe(got5)};协程异常 {_exceptions - exceptionsBefore5} 条");
    }

    // ---------------- 卸载(U1 ~ U4,沿用 AB1) ----------------
    private void UnloadCases()
    {
        // U1 已加载 × UnloadBundle(false)
        Reset();
        Sprite icon1 = Mgr.Load<Sprite>("ui", SpriteName);
        string r1 = TryRun(() => Mgr.UnloadBundle("ui", false));
        Check("U1 卸包(false):不报错、ui 包已卸载、记录已撤", r1 == NoThrow && !IsLoaded("ui") && !HasRecord("ui"), $"{r1};{LoadedBundles()};有记录:{HasRecord("ui")}");
        Check("U1 卸包(false):之前取出的资源还能用", icon1 != null, Describe(icon1));
        Sprite icon1Again = Mgr.Load<Sprite>("ui", SpriteName);
        Check("U1 卸包(false):再取同一个资源,拿到的是新的一份", icon1Again != null && !ReferenceEquals(icon1, icon1Again), $"之前 {Describe(icon1)} / 再取 {Describe(icon1Again)}");

        // U2 已加载 × UnloadBundle(true)
        Reset();
        Sprite icon2 = Mgr.Load<Sprite>("ui", SpriteName);
        bool gotBefore2 = icon2 != null; // 卸之前要先确实取到,否则"== null"不能说明是被卸掉的
        string r2 = TryRun(() => Mgr.UnloadBundle("ui", true));
        Check("U2 卸包(true):之前取出的资源被一起卸掉(== null)", gotBefore2 && r2 == NoThrow && icon2 == null, $"卸之前取到:{gotBefore2};{r2};资源 == null:{icon2 == null}");

        // U3 无记录 × UnloadBundle
        Reset();
        string r3 = TryRun(() => Mgr.UnloadBundle("ui"));
        Check("U3 卸没有记录的包:throw", r3 != NoThrow, r3);

        // U4 UnloadAllBundles 后再 Load
        Reset();
        Mgr.Load<GameObject>("model", "Cube");
        string r4 = TryRun(() => Mgr.UnloadAllBundles(false));
        Check("U4 全部卸载:不报错、已加载的包为 0、记录清空", r4 == NoThrow && LoadedCount() == 0 && RecordCount() == 0, $"{r4};{LoadedBundles()};记录 {RecordCount()} 条");
        GameObject cube4 = null;
        string r4Again = TryRun(() => cube4 = Mgr.Load<GameObject>("model", "Cube"));
        Check("U4 全部卸载后再 Load:正常拿到", r4Again == NoThrow && cube4 != null, $"{r4Again};{Describe(cube4)}");
    }

    // ---------------- 交错场景(S1、S3 ~ S6;S2 见 A2) ----------------
    private IEnumerator InterleaveCases()
    {
        // S1 A 异步加载 ui 中,同一帧 B 同步 Load ui(ui 没有依赖包,A 正卡在加载 ui 本身)
        Reset();
        int duplicateBefore1 = _duplicateErrors;
        int exceptionsBefore1 = _exceptions;
        int countA1 = 0;
        Sprite gotA1 = null;
        Mgr.LoadAsync<Sprite>("ui", SpriteName, sprite =>
        {
            countA1++;
            gotA1 = sprite;
        });
        Sprite gotB1 = null;
        string rB1 = TryRun(() => gotB1 = Mgr.Load<Sprite>("ui", SpriteName));
        Check("S1 B 同步强制完成:当场拿到资源、不报错", rB1 == NoThrow && gotB1 != null, $"{rB1};B 拿到 {Describe(gotB1)}");
        yield return WaitFor(() => countA1 > 0);
        yield return WaitFrames(5);
        Check("S1 没有重复加载报错、没有异常", _duplicateErrors == duplicateBefore1 && _exceptions == exceptionsBefore1, $"重复加载报错 {_duplicateErrors - duplicateBefore1} 条;异常 {_exceptions - exceptionsBefore1} 条");
        Check("S1 A 的回调照常执行 1 次", countA1 == 1 && gotA1 != null, $"次数 {countA1};A 收到 {Describe(gotA1)}");
        Check("S1 A、B 拿到的是同一个资源对象", gotA1 != null && ReferenceEquals(gotA1, gotB1), $"A {Describe(gotA1)} / B {Describe(gotB1)}");

        // S3 同一帧两个异步请求要同一个不存在的包:A 加载失败,C 在等
        Reset();
        int exceptionsBefore3 = _exceptions;
        int countA3 = 0;
        int countC3 = 0;
        Mgr.LoadAsync<GameObject>(MissingBundle, "X", obj => countA3++);
        Mgr.LoadAsync<GameObject>(MissingBundle, "X", obj => countC3++);
        yield return WaitFrames(SettleFrames);
        Check("S3 A、C 都 throw(协程异常 2 条)、回调都不执行", _exceptions - exceptionsBefore3 == 2 && countA3 == 0 && countC3 == 0, $"协程异常 {_exceptions - exceptionsBefore3} 条;A 回调 {countA3} 次 / C 回调 {countC3} 次");
        Check("S3 记录已撤", !HasRecord(MissingBundle), $"有记录:{HasRecord(MissingBundle)}");

        // S4 A 异步加载 ui 中,同一帧 UnloadBundle("ui")
        // 实测两种结果都可能出现,取决于强制完成时 A 会不会被当场唤醒:
        //   当场唤醒:A 在撤记录前就存好包、开始取资源,Unload(false) 等它取完,A 照常拿到资源(回调 1 次、无异常)
        //   没被当场唤醒:A 之后恢复发现记录没了,throw(回调 0 次、异常 1 条)
        // 两种都算对,观察项里记下这次走的是哪一种
        Reset();
        int exceptionsBefore4 = _exceptions;
        int countA4 = 0;
        Sprite gotA4 = null;
        Mgr.LoadAsync<Sprite>("ui", SpriteName, sprite =>
        {
            countA4++;
            gotA4 = sprite;
        });
        string rU4 = TryRun(() => Mgr.UnloadBundle("ui", false));
        Check("S4 卸载加载中的包:不报错、包已卸载、记录已撤", rU4 == NoThrow && !IsLoaded("ui") && !HasRecord("ui"), $"{rU4};{LoadedBundles()};有记录:{HasRecord("ui")}");
        yield return WaitFrames(SettleFrames);
        bool wokeOnTheSpot = countA4 == 1 && gotA4 != null && _exceptions == exceptionsBefore4;
        bool threwLater = countA4 == 0 && _exceptions - exceptionsBefore4 == 1;
        Check("S4 A 的结果是两种之一(拿到资源 / throw),不会两样都发生、也不会都不发生", wokeOnTheSpot || threwLater, $"A 回调 {countA4} 次;收到 {Describe(gotA4)};协程异常 {_exceptions - exceptionsBefore4} 条");
        Observe("S4 这次 A 走的是", wokeOnTheSpot ? "当场被唤醒,照常拿到资源" : threwLater ? "之后恢复,发现记录没了而 throw" : "两种都不是");

        // S5 A 异步加载 model 中、C 在等,同一帧 UnloadAllBundles
        Reset();
        int exceptionsBefore5 = _exceptions;
        int duplicateBefore5 = _duplicateErrors;
        int countA5 = 0;
        int countC5 = 0;
        Mgr.LoadAsync<GameObject>("model", "Cube", obj => countA5++);
        Mgr.LoadAsync<GameObject>("model", "Cube", obj => countC5++);
        string rClear5 = TryRun(() => Mgr.UnloadAllBundles(false));
        Check("S5 清空:不报错、句柄集合清空、记录清空", rClear5 == NoThrow && RunningCount() == 0 && RecordCount() == 0, $"{rClear5};句柄 {RunningCount()} 个;记录 {RecordCount()} 条");
        yield return WaitFrames(SettleFrames);
        Check("S5 回调都不执行、没有异常", countA5 == 0 && countC5 == 0 && _exceptions == exceptionsBefore5, $"A {countA5} 次 / C {countC5} 次;异常 {_exceptions - exceptionsBefore5} 条");
        Check("S5 清空 30 帧后没有孤儿包", LoadedCount() == 0, LoadedBundles());
        GameObject again5 = null;
        string rAgain5 = TryRun(() => again5 = Mgr.Load<GameObject>("model", "Cube"));
        Check("S5 之后再 Load model:正常拿到、没有重复加载报错", rAgain5 == NoThrow && again5 != null && _duplicateErrors == duplicateBefore5, $"{rAgain5};{Describe(again5)};重复加载报错 {_duplicateErrors - duplicateBefore5} 条");

        // S6 A 异步加载一个不存在的包中,同一帧 B 同步 Load 这个包
        Reset();
        int exceptionsBefore6 = _exceptions;
        int countA6 = 0;
        Mgr.LoadAsync<GameObject>(MissingBundle, "X", obj => countA6++);
        string rB6 = TryRun(() => Mgr.Load<GameObject>(MissingBundle, "X"));
        Check("S6 B 强制完成拿到 null:throw 自己的报错、记录已撤", IsOwnThrow(rB6) && !HasRecord(MissingBundle), $"{rB6};有记录:{HasRecord(MissingBundle)}");
        yield return WaitFrames(SettleFrames);
        Check("S6 A 的回调不执行、A 抛 1 条异常", countA6 == 0 && _exceptions - exceptionsBefore6 == 1, $"A 回调 {countA6} 次;协程异常 {_exceptions - exceptionsBefore6} 条");
    }

    // ---------------- 依赖包交错(D1、D2) ----------------
    private IEnumerator DependencyCases()
    {
        // D1 A 异步要 mat(依赖包)中,同一帧 B 异步要 model
        Reset();
        int duplicateBefore1 = _duplicateErrors;
        int countA1 = 0;
        int countB1 = 0;
        Material gotMat1 = null;
        GameObject gotCube1 = null;
        Mgr.LoadAsync<Material>("mat", "Red", material =>
        {
            countA1++;
            gotMat1 = material;
        });
        Mgr.LoadAsync<GameObject>("model", "Cube", go =>
        {
            countB1++;
            gotCube1 = go;
        });
        yield return WaitFor(() => countA1 > 0 && countB1 > 0);
        yield return WaitFrames(5);
        Check("D1 两个回调各执行 1 次", countA1 == 1 && countB1 == 1, $"A {countA1} 次 / B {countB1} 次");
        Check("D1 没有重复加载报错、包各一个", _duplicateErrors == duplicateBefore1 && LoadedCount() == 3, $"重复加载报错 {_duplicateErrors - duplicateBefore1} 条;{LoadedBundles()}");
        Check("D1 预设体上的材质就是 A 取出的那个", gotMat1 != null && ReferenceEquals(gotMat1, MaterialOf(gotCube1)), $"A 取的 {Describe(gotMat1)} / 预设体上的 {Describe(MaterialOf(gotCube1))}");
        Check("H 请求都完成后:协程句柄集合为空", RunningCount() == 0, $"集合里还有 {RunningCount()} 个");

        // D2 A 异步要 mat 中,同一帧 B 同步要 model(B 要对依赖包 mat 强制完成)
        Reset();
        int duplicateBefore2 = _duplicateErrors;
        int exceptionsBefore2 = _exceptions;
        int countA2 = 0;
        Mgr.LoadAsync<Material>("mat", "Red", material => countA2++);
        GameObject cube2 = null;
        string rB2 = TryRun(() => cube2 = Mgr.Load<GameObject>("model", "Cube"));
        Material material2 = MaterialOf(cube2);
        Check("D2 B 当场拿到预设体,材质接上了", rB2 == NoThrow && cube2 != null && material2 != null && material2.name == "Red", $"{rB2};{Describe(cube2)};材质 {Describe(material2)}");
        yield return WaitFor(() => countA2 > 0);
        yield return WaitFrames(5);
        Check("D2 A 的回调照常 1 次;没有重复加载报错、没有异常", countA2 == 1 && _duplicateErrors == duplicateBefore2 && _exceptions == exceptionsBefore2, $"A {countA2} 次;重复加载报错 {_duplicateErrors - duplicateBefore2} 条;异常 {_exceptions - exceptionsBefore2} 条");
    }

    // ---------------- 观察项(X1) ----------------
    private IEnumerator ObserveCases()
    {
        // X1 包已加载,正在从包里取资源(LoadAssetAsync 还没完成)时卸掉这个包
        // 直接对 Unity 的包调 LoadAssetAsync,确保卸载发生在"取资源途中";要观察的是 Unity 自己的行为
        Reset();
        Mgr.Load<GameObject>("model", "Cube"); // 先把包加载好
        AssetBundle model = AssetBundle.GetAllLoadedAssetBundles().FirstOrDefault(b => b.name == "model");
        if (model == null)
        {
            Observe("X1 取资源途中卸包", "model 包没加载上,跳过");
            yield break;
        }
        int exceptionsBefore = _exceptions;
        AssetBundleRequest request = model.LoadAssetAsync<GameObject>("Cube");
        bool doneBeforeUnload = request.isDone;
        string rU = TryRun(() => Mgr.UnloadBundle("model", false));
        yield return request;
        yield return WaitFrames(5);
        Observe("X1 取资源途中卸包(false)", $"卸载前已完成:{doneBeforeUnload};卸载:{rU};request 结果 {Describe(request.asset)};新增异常 {_exceptions - exceptionsBefore} 条(你的预期:资源被卸载)");
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

    private static Material MaterialOf(GameObject go)
    {
        if (go == null)
            return null;
        MeshRenderer meshRenderer = go.GetComponent<MeshRenderer>();
        return meshRenderer != null ? meshRenderer.sharedMaterial : null;
    }

    // 反射读 ABManager 的私有字段
    private object GetPrivateField(string fieldName)
    {
        FieldInfo field = Mgr.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        return field?.GetValue(Mgr);
    }

    private bool HasRecord(string bundleName)
    {
        return GetPrivateField("_bundleDic") is IDictionary dic && dic.Contains(bundleName);
    }

    private int RecordCount()
    {
        return GetPrivateField("_bundleDic") is IDictionary dic ? dic.Count : -1;
    }

    private int RunningCount()
    {
        object set = GetPrivateField("_runningCoroutines");
        if (set == null)
            return -1;
        return (int)set.GetType().GetProperty("Count").GetValue(set);
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