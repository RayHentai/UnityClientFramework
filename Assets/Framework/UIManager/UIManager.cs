using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

public enum E_UILayer
{
    Bottom,
    Middle,
    Top,
    System,
}
public enum E_PanelTargetState
{
    Shown,
    Hidden,
    Destroyed,
}
public class UIManager : SingletonBase<UIManager>
{
    private abstract class PanelInfoBase { }
    private class PanelInfo<T> : PanelInfoBase where T : PanelBase
    {
        public T panel;
        public UnityAction<T> callback;
        public E_PanelTargetState targetState;
        public bool isLoading;
        public PanelInfo(UnityAction<T> callback)
        {
            this.callback = callback;
        }
    }
    private Canvas _canvas;
    private EventSystem _eventSystem;
    private Camera _uiCamera;
    private Transform _bottomLayer, _middleLayer, _topLayer, _systemLayer;
    private Dictionary<string, PanelInfoBase> _panelDic = new();

    private const string UIPath = "UI/";
    private const string CanvasPath = UIPath + "Canvas";
    private const string CameraPath = UIPath + "UICamera";
    private const string EventSystemPath = UIPath + "EventSystem";
    private UIManager()
    {
        Init();
    }
    private void Init()
    {
        _canvas = GameObject.Instantiate(Resources.Load<GameObject>(CanvasPath)).GetComponent<Canvas>();

        if (_canvas.renderMode == RenderMode.ScreenSpaceCamera)
        {
            _uiCamera = GameObject.Instantiate(Resources.Load<GameObject>(CameraPath)).GetComponent<Camera>();
            _canvas.worldCamera = _uiCamera;
            GameObject.DontDestroyOnLoad(_uiCamera);
        }
        _eventSystem = GameObject.Instantiate(Resources.Load<GameObject>(EventSystemPath)).GetComponent<EventSystem>();

        _bottomLayer = _canvas.transform.Find("BottomLayer");
        _middleLayer = _canvas.transform.Find("MiddleLayer");
        _topLayer = _canvas.transform.Find("TopLayer");
        _systemLayer = _canvas.transform.Find("SystemLayer");

        if (_bottomLayer == null)
            throw new System.Exception($"Canvas 下 没有找到 名字为 BottomLayer 的子根物体");
        if (_middleLayer == null)
            throw new System.Exception($"Canvas 下 没有找到 名字为 MiddleLayer 的子根物体");
        if (_topLayer == null)
            throw new System.Exception($"Canvas 下 没有找到 名字为 TopLayer 的子根物体");
        if (_systemLayer == null)
            throw new System.Exception($"Canvas 下 没有找到 名字为 SystemLayer 的子根物体");

        GameObject.DontDestroyOnLoad(_canvas);
        GameObject.DontDestroyOnLoad(_eventSystem);

    }
    private Transform GetLayer(E_UILayer layer)
    {
        switch (layer)
        {
            case E_UILayer.Bottom:
                return _bottomLayer;
            case E_UILayer.Middle:
                return _middleLayer;
            case E_UILayer.Top:
                return _topLayer;
            case E_UILayer.System:
                return _systemLayer;
            default:
                throw new System.Exception($"层级 {layer} 在方法 {nameof(GetLayer)} 中 没有对应的分支");
        }
    }
    private void InitPanel<T>(GameObject panelObj, PanelInfo<T> info) where T : PanelBase
    {
        info.panel = panelObj.GetComponent<T>();
        switch (info.targetState)
        {
            case E_PanelTargetState.Shown:
                info.panel.OnShow();
                info.callback?.Invoke(info.panel);
                info.callback = null;
                break;
            case E_PanelTargetState.Hidden:
                panelObj.SetActive(false);
                info.targetState = E_PanelTargetState.Shown;
                break;
            case E_PanelTargetState.Destroyed:
            default:
                throw new System.Exception($"状态 {info.targetState} 在 方法 {nameof(InitPanel)} 中不存在分支 ");
        }
    }
    private void ThrowIfPanelDestroyedExternally<T>(PanelInfo<T> info, string name) where T : PanelBase
    {
        if (info.panel == null)
            throw new System.Exception($"dic 中 key {name} 类型为 {typeof(T).Name} 的 物体 通过 非管理器手段 移除 但是 key 还留在字典中 ");
    }
    private bool TryValidatePanelPrefab<T>(GameObject prefab, string prefabName, out string error) where T : PanelBase
    {
        error = null;
        if (prefab == null)
        {
            error = $"路径 Resources/{UIPath} 下 不存在 名为 {prefabName} 的 Prefab";
            return false;
        }
        if (!prefab.activeSelf)
        {
            error = $"Prefab {prefab.name} 处于失活状态";
            return false;
        }
        if (prefab.GetComponent<T>() == null)
        {
            error = $"Prefab {prefab.name} 没有挂载同名脚本{typeof(T)}";
            return false;
        }
        return true;
    }
    public void ShowPanel<T>(E_UILayer layer = E_UILayer.Middle, UnityAction<T> callback = null, bool shouldAsync = false) where T : PanelBase
    {
        string name = typeof(T).Name;
        if (_panelDic.TryGetValue(name, out var panelInfoBase))
        {
            if (panelInfoBase is PanelInfo<T> panelInfo)
            {
                if (panelInfo.isLoading)
                {
                    panelInfo.callback += callback;
                    panelInfo.targetState = E_PanelTargetState.Shown;
                }
                else
                {
                    ThrowIfPanelDestroyedExternally(panelInfo, name);
                    if (!panelInfo.panel.gameObject.activeSelf)
                    {
                        panelInfo.panel.gameObject.SetActive(true);
                        panelInfo.panel.OnShow();
                    }
                    panelInfo.callback += callback;
                    panelInfo.callback?.Invoke(panelInfo.panel);
                    panelInfo.callback = null;
                }
            }
            else
                throw new System.Exception($"dic 中 key {name} 存储的类型是 {panelInfoBase.GetType().FullName}, 而传入的类型是 {typeof(PanelInfo<T>).FullName} ");
        }
        else
        {
            GameObject panelObj;
            GameObject panelRes;
            PanelInfo<T> panelInfo;
            if (shouldAsync)
            {
                panelInfo = new PanelInfo<T>(callback);
                panelInfo.isLoading = true;
                _panelDic.Add(name, panelInfo);
                ResourceRequest request = Resources.LoadAsync<GameObject>(UIPath + name);
                request.completed += (op) =>
                {
                    panelRes = request.asset as GameObject;
                    if (!TryValidatePanelPrefab<T>(panelRes, name, out var error))
                    {
                        _panelDic.Remove(name);
                        throw new System.Exception(error);
                    }
                    panelInfo.isLoading = false;
                    switch (panelInfo.targetState)
                    {
                        case E_PanelTargetState.Shown:
                        case E_PanelTargetState.Hidden:
                            panelObj = GameObject.Instantiate(panelRes, GetLayer(layer), false);

                            InitPanel<T>(panelObj, panelInfo);
                            break;
                        case E_PanelTargetState.Destroyed:
                            _panelDic.Remove(name);
                            break;
                        default:
                            throw new System.Exception($"状态 {panelInfo.targetState} 在 方法 {nameof(ShowPanel)} 中不存在分支 ");
                    }
                };
            }
            else
            {
                panelInfo = new PanelInfo<T>(callback);
                panelRes = Resources.Load<GameObject>((UIPath + name));
                if (!TryValidatePanelPrefab<T>(panelRes, name, out var error))
                    throw new System.Exception(error);
                _panelDic.Add(name, panelInfo);
                panelObj = GameObject.Instantiate(panelRes, GetLayer(layer), false);
                InitPanel<T>(panelObj, panelInfo);
            }
        }
    }
    public void HidePanel<T>(bool shouldDestroy = true) where T : PanelBase
    {
        string name = typeof(T).Name;
        if (_panelDic.TryGetValue(name, out var panelInfoBase))
        {
            if (panelInfoBase is PanelInfo<T> panelInfo)
            {
                if (shouldDestroy)
                {
                    if (panelInfo.isLoading)
                        panelInfo.targetState = E_PanelTargetState.Destroyed;
                    else
                    {
                        ThrowIfPanelDestroyedExternally(panelInfo, name);
                        if (panelInfo.panel.gameObject.activeSelf)
                            panelInfo.panel.OnHide();
                        GameObject.Destroy(panelInfo.panel.gameObject);
                        _panelDic.Remove(name);
                    }
                }
                else
                {
                    if (panelInfo.isLoading)
                        panelInfo.targetState = E_PanelTargetState.Hidden;
                    else
                    {
                        ThrowIfPanelDestroyedExternally(panelInfo, name);
                        if (panelInfo.panel.gameObject.activeSelf)
                            panelInfo.panel.OnHide();
                        panelInfo.panel.gameObject.SetActive(false);
                    }
                }
            }
            else
                throw new System.Exception($"dic 中 key {name} 存储的类型是 {panelInfoBase.GetType().FullName}, 而传入的类型是 {typeof(PanelInfo<T>).FullName} ");
        }
    }
    public T GetPanel<T>() where T : PanelBase
    {
        string name = typeof(T).Name;
        if (_panelDic.TryGetValue(name, out var panelInfoBase))
        {
            if (panelInfoBase is PanelInfo<T> panelInfo)
            {
                if (panelInfo.isLoading)
                    throw new System.Exception($"dic 中 key {name} 类型为 {typeof(T).Name} 的 物体 当前正在异步加载 ");
                ThrowIfPanelDestroyedExternally(panelInfo, name);
                if (panelInfo.panel.gameObject.activeSelf)
                    return panelInfo.panel;
                else
                    throw new System.Exception($"dic 中 key {name} 类型为 {typeof(T).Name} 的 物体 当前处于失活状态 ");
            }
            else
                throw new System.Exception($"dic 中 key {name} 存储的类型是 {panelInfoBase.GetType().FullName}, 而传入的类型是 {typeof(PanelInfo<T>).FullName} ");
        }
        else
            throw new System.Exception($"没有在 dic 中 找到 key 为 {name} 的面板");
    }
    public bool TryGetPanel<T>(out T panel) where T : PanelBase
    {
        string name = typeof(T).Name;
        panel = null;
        if (_panelDic.TryGetValue(name, out var panelInfoBase))
        {
            if (panelInfoBase is PanelInfo<T> panelInfo)
            {

                if (panelInfo.isLoading)
                {
                    return false;
                }
                else
                {
                    ThrowIfPanelDestroyedExternally(panelInfo, name);
                    if (panelInfo.panel.gameObject.activeSelf)
                    {
                        panel = panelInfo.panel;
                        return true;
                    }
                    else
                        return false;
                }
            }
            else
                throw new System.Exception($"dic 中 key {name} 存储的类型是 {panelInfoBase.GetType().FullName}, 而传入的类型是 {typeof(PanelInfo<T>).FullName} ");
        }
        else
        {
            return false;
        }
    }
}
