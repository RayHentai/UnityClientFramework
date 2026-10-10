using System.Collections;
using System.Collections.Generic;
using UnityEngine.Events;

public enum E_EventType
{
    Monster_Awake,
    UIManager_Test,
    Gold_Changed,
    Item_Added,
}
public class EventCenter : SingletonLazyBase<EventCenter>
{
    private interface IEventInfo { }
    private class EventInfo : IEventInfo
    {
        public UnityAction action;
        public EventInfo(UnityAction func)
        {
            action += func;
        }
    }
    private class EventInfo<T> : IEventInfo
    {
        public UnityAction<T> action;
        public EventInfo(UnityAction<T> func)
        {
            action += func;
        }
    }
    private Dictionary<E_EventType, IEventInfo> _eventDic = new();
    public void EventTrigger(E_EventType type)
    {
        if (_eventDic.TryGetValue(type, out var eventInfo))
        {
            if (eventInfo is EventInfo info)
                info.action?.Invoke();
            else
                throw new System.Exception($"事件 {type} 以无参方式触发,但该事件已注册为 {eventInfo.GetType()}");
        }
    }
    public void EventTrigger<T>(E_EventType type, T value)
    {
        if (_eventDic.TryGetValue(type, out var eventInfo))
        {
            if (eventInfo is EventInfo<T> info)
                info.action?.Invoke(value);
            else
                throw new System.Exception($"事件 {type} 触发时传入参数类型 {typeof(T).Name},但该事件已注册为 {eventInfo.GetType()}");
        }
    }
    public void AddEventListener(E_EventType type, UnityAction func)
    {
        if (_eventDic.TryGetValue(type, out var eventInfo))
        {
            if (eventInfo is EventInfo info)
                info.action += func;
            else
                throw new System.Exception($"事件 {type} 添加了无参回调,但该事件已注册为 {eventInfo.GetType()}");
        }
        else
            _eventDic.Add(type, new EventInfo(func));
    }
    public void AddEventListener<T>(E_EventType type, UnityAction<T> func)
    {
        if (_eventDic.TryGetValue(type, out var eventInfo))
        {
            if (eventInfo is EventInfo<T> info)
                info.action += func;
            else
                throw new System.Exception($"事件 {type} 添加了参数为 {typeof(T).Name} 的回调,但该事件已注册为 {eventInfo.GetType()}");
        }
        else
            _eventDic.Add(type, new EventInfo<T>(func));
    }
    public void RemoveEventListener(E_EventType type, UnityAction func)
    {
        if (_eventDic.TryGetValue(type, out var eventInfo)) 
        {
            if (eventInfo is EventInfo info) 
                info.action -= func;
            else
                throw new System.Exception($"事件 {type} 移除无参回调,但该事件已注册为 {eventInfo.GetType()}");
        }
    }
    public void RemoveEventListener<T>(E_EventType type, UnityAction<T> func)
    {
        if (_eventDic.TryGetValue(type, out var eventInfo))
        {
            if (eventInfo is EventInfo<T> info)
                info.action -= func;
            else
                throw new System.Exception($"事件 {type} 移除参数为 {typeof(T).Name} 的回调,但该事件已注册为 {eventInfo.GetType()}");
        }
    }
    public void Clear(E_EventType type)
    {
        _eventDic.Remove(type);
    }
}
