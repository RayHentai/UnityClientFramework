using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public abstract class PanelBase : MonoBehaviour
{
    private Dictionary<string, UIBehaviour> _controlDic = new();
    private static readonly Dictionary<string, Type> s_prefixTypes = new()
    {
        { "Btn", typeof(Button)},
        { "Tog", typeof(Toggle)},
        { "Sld", typeof(Slider)},
        { "Ipt", typeof(InputField)},
        { "Drp", typeof(Dropdown)},
        { "Scr", typeof(ScrollRect)},
        { "Txt", typeof(Text)},
        { "Img", typeof(Image)},
        //待添加...
    };
    protected virtual void Awake()
    {
        Init();
    }
    private void Init()
    {
        FindChildControls<Button>();
        FindChildControls<Toggle>();
        FindChildControls<Slider>();
        FindChildControls<InputField>();
        FindChildControls<Dropdown>();
        FindChildControls<ScrollRect>();
        FindChildControls<Text>();
        FindChildControls<Image>();
    }
    private void FindChildControls<T>() where T : UIBehaviour
    {
        T[] controls = GetComponentsInChildren<T>(true);
        foreach (T control in controls)
        {
            string name = control.gameObject.name;
            int separatorIndex = name.IndexOf("_");
            if (separatorIndex <= 0) continue;

            string key = name.Substring(0, separatorIndex);
            if (!s_prefixTypes.TryGetValue(key, out var type))
                throw new System.Exception($"白名单中不存在 前缀为 {key + '_'} 的组件");

            if (type != typeof(T)) continue;

            if (_controlDic.ContainsKey(name))
                throw new System.Exception($"名为 {name} ， 类型为 {typeof(T)} 的组件已被记录");

            _controlDic.Add(name, control);
            switch (control)
            {
                case Button btn:
                    btn.onClick.AddListener(() => { OnClick(name); });
                    break;
                case Toggle tog:
                    tog.onValueChanged.AddListener((value) => { OnToggleValueChanged(name, value); });
                    break;
                case Slider sld:
                    sld.onValueChanged.AddListener((value) => { OnSliderValueChanged(name, value); });
                    break;

                case InputField:
                case ScrollRect:
                case Dropdown:
                case Text:
                case Image:
                    break;

                default:
                    throw new System.Exception($"方法 {nameof(FindChildControls)} 中未更新 类型 {typeof(T)} 的分支");
            }
        }
    }
    public T GetControl<T>(string controlName) where T : UIBehaviour
    {
        if (_controlDic.TryGetValue(controlName, out var control))
        {
            if (control is T ctrl)
                return ctrl;
            else
                throw new System.Exception($"key {controlName} 存储的类型是{control.GetType().FullName}，而传入的类型是{typeof(T).FullName}");
        }
        else
            throw new System.Exception($"没有在 dic 中找到 key 为 {controlName} 的控件");
    }
    public abstract void OnShow();
    public abstract void OnHide();
    protected virtual void OnClick(string btnName) { }
    protected virtual void OnToggleValueChanged(string togName, bool value) { }
    protected virtual void OnSliderValueChanged(string sldName, float value) { }
}
