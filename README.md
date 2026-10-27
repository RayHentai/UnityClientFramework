# Unity 客户端基础框架

> 个人学习项目，Unity 2022.3。单例到 UI 管理五个模块：学完唐老狮的程序基础框架课后，合上教材独立重写；资源加载在 AI 协助下编写，设计和测试用例由我负责。数据持久化开发中。

## 模块一览

| 模块 | 解决什么问题 | 状态 |
|---|---|---|
| 单例基类 | 管理器全局只要一份，并且要有统一入口 | 完成 |
| 公共 Mono | 不继承 MonoBehaviour 的管理器也能用帧更新和协程 | 完成 |
| 对象池 | 频繁 Instantiate / Destroy 带来的内存分配和 GC 开销 | 完成 |
| 事件中心 | 模块之间直接引用造成的耦合 | 完成 |
| UI 管理 | 每个面板各自找控件、各自写加载和显示隐藏，重复又容易出错 | 完成 |
| 资源加载 | 统一 Resources / AssetBundle / Addressables 三种加载方式，管住资源什么时候加载、什么时候卸载 | 完成 |
| 数据持久化 | 存档读档 | 开发中 |

## 单例基类

- **解决什么问题**：UI、资源、事件中心这类管理器全局只需要一份，还要有统一的访问入口。
- **怎么设计**：分四种：

| 类 | 继承 MonoBehaviour | 怎么创建 | 适合 |
|---|---|---|---|
| `SingletonBase<T>` | 否 | 第一次访问时用反射调私有无参构造，双重检查加锁 | 纯逻辑管理器，要防止外部 new |
| `SingletonLazyBase<T>` | 否 | `Lazy<T>` 延迟创建 | 纯逻辑管理器，写法最简 |
| `SingletonMonoBase<T>` | 是 | 手动挂在场景物体上，Awake 里去重 | 要在 Inspector 里配参数 |
| `SingletonAutoMonoBase<T>` | 是 | 第一次访问时自动建物体、挂脚本 | 要用生命周期，但不用配参数（如公共 Mono） |

- **为什么这样设计**：选哪种看两件事：要不要用 Unity 的生命周期（要就继承 MonoBehaviour），要不要在 Inspector 里配（要就手动挂载）。`SingletonLazyBase` 写法简洁，但 `new()` 约束要求公开的无参构造，挡不住外部 new；`SingletonBase` 用反射调私有构造，能挡住，代价是反射和加锁。
- **使用示例**：

```csharp
public class UIManager : SingletonBase<UIManager>
{
    private UIManager() { }
}

UIManager.Instance.ShowPanel<BagPanel>();
```

## 公共 Mono

- **解决什么问题**：对象池、资源加载这类不继承 MonoBehaviour 的管理器没有 Update，也开不了协程。
- **怎么设计**：`MonoManager` 继承自动挂载式单例，按 Update / LateUpdate / FixedUpdate 各存一个委托，在自己真正的生命周期函数里调用；协程的开关也由它转发。
- **为什么这样设计**：注册只是把方法存起来，要靠真正的 Update 每帧调用才会跑。存委托的字典不对外公开，只能通过 Add / Remove 修改。
- **使用示例**：

```csharp
MonoManager.Instance.AddUpdateListener(MonoManager.E_UpdateType.Update, OnUpdate);
MonoManager.Instance.StartCor(LoadRoutine());
```

## 对象池

- **解决什么问题**：子弹、特效这类对象频繁创建销毁：Instantiate 要分配内存，Destroy 产生垃圾，都会加重 GC。
- **怎么设计**：两套池分开管。
    - **GameObject 池**：按名字分类，每类一个"抽屉"，闲置的放栈里，在用的记在列表里。上限 `maxNum` 写在预设体的 `PoolObject` 组件上，在用的达到上限又没有闲置时，复用最早取出的那个。打开布局开关后，闲置对象按类别挂在 Pool 根物体下，Hierarchy 里一目了然。
    - **纯 C# 对象池**：按类型名分类，用队列存。
- **为什么这样设计**：
    - GameObject 会被 Unity 销毁，引用可能悬空；纯 C# 对象只要有引用就一直在。两种对象生命周期不同，所以分开管，清理规则也不同。
    - 上限加在"新建"这一步：有闲置的永远优先复用，只有要新建时才看有没有到上限。
    - 一个名字三处用：字典的 key、Resources 加载路径、物体名（回收时按 `obj.name` 找回抽屉）。
- **使用示例**：

```csharp
GameObject bullet = PoolManager.Instance.GetGameObj("Bullet");
PoolManager.Instance.PushGameObj(bullet);
```

## 事件中心

- **解决什么问题**：玩家死亡时，UI、成就、音效都要响应；互相直接引用，加一个响应就要改一处发生方的代码。
- **怎么设计**：`Dictionary<E_EventType, IEventInfo>`。无参事件存 `EventInfo`，带参事件存 `EventInfo<T>`，两者都实现 `IEventInfo`，放进同一个字典。触发、添加、移除时参数类型对不上，直接抛异常。
- **为什么这样设计**：
    - 发生方只负责触发，响应方自己订阅；新加响应不用改发生方。
    - 用泛型包装类而不是 object 传参：值类型参数不装箱，类型转换挪到容器这一层。
    - 一个事件只允许一种参数签名，对不上当场报错，不留到运行中途才出问题。
    - 事件名用枚举，避免字符串拼错。
- **使用示例**：

```csharp
EventCenter.Instance.AddEventListener<int>(E_EventType.Gold_Changed, OnGoldChanged);
EventCenter.Instance.EventTrigger(E_EventType.Gold_Changed, 100);
EventCenter.Instance.RemoveEventListener<int>(E_EventType.Gold_Changed, OnGoldChanged);
```

## UI 管理

- **解决什么问题**：每个面板都要自己找控件、自己写加载和显示隐藏，重复又容易出错。
- **怎么设计**：
    - `PanelBase`：Awake 时按名字前缀（`Btn_`、`Tog_`、`Sld_` 等，前缀在白名单里）自动收集子控件；按钮、开关、滑条的事件统一转到 `OnClick` 等虚方法。
    - `UIManager`：一个面板类型一份，类名做 key；分 Bottom / Middle / Top / System 四层；支持同步和异步加载。
    - `UIBehaviourExtensions`：给任意控件加 EventTrigger 事件（拖拽、长按等）。
- **为什么这样设计**：
    - 记最终状态，不记操作过程：异步加载还没完成时连续调用 Show / Hide，只按最后一次的结果处理。
    - "正在加载"单独用 `isLoading` 记：面板为 null 既可能是还没加载完，也可能是被外部销毁了，一个 null 不能代表两种情况。
    - 回调和 OnShow 分开：回调是这一次打开要做的事，OnShow 是面板每次真正显示时自己要做的事；面板已经开着再 Show，只执行回调。
    - 先校验再登记：预设体不存在、处于失活、没挂对应脚本，都在进字典之前拦下。
- **使用示例**：

```csharp
UIManager.Instance.ShowPanel<BagPanel>(E_UILayer.Middle, panel => { /* 这一次打开要做的事 */ }, shouldAsync: true);
UIManager.Instance.HidePanel<BagPanel>(shouldDestroy: false);
```

## 资源加载

- **解决什么问题**：统一三种加载方式的接口，并管住资源的生命周期：重复请求不重复加载，没人用了再卸载。
- **怎么设计**：

| 管理器 | 加载 | 生命周期 |
|---|---|---|
| `ResourcesManager` | 同步 / 异步，key 为"路径_类型" | 引用计数，归零并要求立即卸载时才卸 |
| `ABManager` | 先从主包的 Manifest 取依赖，依赖包加载完再加载目标包；同步 / 异步 | 按包卸载，或全部卸载 |
| `AddressablesManager` | 单个加载，或多个 key（地址、标签）按 MergeMode 合并加载；同步 / 异步 | 同一个 key 共用一个句柄，自己记引用计数，归零才 Release |

- **为什么这样设计**：
    - 异步加载还没完成时又有请求：只把回调挂上去，不重复加载；同步请求撞上进行中的异步加载，停掉协程，当场同步加载。
    - Addressables 对同一个 key 只记一次句柄，有几个人在用要自己数。
    - 预设体和组件不能用 `Resources.UnloadAsset` 单独卸，计数归零后交给 `UnloadUnusedAssets`。
    - 清理没人用的资源时跳过还在加载的记录：否则旧协程加载完，会撤掉之后新建的同名记录（这个 bug 写了回归测试）。
    - AB 包目前没做包级引用计数，卸载时机由调用方决定。
- **怎么测试**：`ResourcesManager/Test/` 下每个管理器一份测试脚本，用例编号对应测试清单，结果按 `[PASS]` / `[FAIL]` / `[OBSERVE]` 输出，最后一行汇总；私有状态用反射读出来核对。
- **使用示例**：

```csharp
ResourcesManager.Instance.LoadAsync<Sprite>("ResTest/Item", sprite => icon.sprite = sprite);
ResourcesManager.Instance.UnloadAsset<Sprite>("ResTest/Item", isUnloadImmediately: true);

GameObject cube = ABManager.Instance.Load<GameObject>("model", "Cube");
```

## 数据持久化

开发中，计划覆盖 PlayerPrefs、XML、JSON、二进制四种存储方式。

## 运行

- Unity 版本：2022.3.62f3
- 资源加载的测试：把 `Assets/Framework/ResourcesManager/Test/` 里的测试脚本挂到场景物体上运行，准备步骤写在每个脚本开头的注释里。AB 包先用菜单 Tools/AB/Build (PC) 打包。
