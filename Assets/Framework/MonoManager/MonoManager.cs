using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class MonoManager : SingletonAutoMonoBase<MonoManager>
{
    public enum E_UpdateType 
    {
        Update = 0,
        LateUpdate = 1,
        FixedUpdate = 2,
    }
    private Dictionary<E_UpdateType, UnityAction> _eventDic;

    private void Awake()
    {
        _eventDic = new Dictionary<E_UpdateType, UnityAction>();
        _eventDic.Add(E_UpdateType.Update, null);
        _eventDic.Add(E_UpdateType.LateUpdate, null);
        _eventDic.Add(E_UpdateType.FixedUpdate, null);
    }
    private void Update()
    {
        if (_eventDic[E_UpdateType.Update] == null)
            return;
        try
        {
            foreach (var d in _eventDic[E_UpdateType.Update].GetInvocationList())
                (d as UnityAction)?.Invoke();
        }
        catch (System.Exception e)
        {

            Debug.LogError(e);
        }
    }
    private void LateUpdate()
    {
        if (_eventDic[E_UpdateType.LateUpdate] == null)
            return;
        try
        {
            foreach (var d in _eventDic[E_UpdateType.LateUpdate].GetInvocationList())
                (d as UnityAction)?.Invoke();
        }
        catch (System.Exception e)
        {

            Debug.LogError(e);
        }
    }
    private void FixedUpdate()
    {
        if (_eventDic[E_UpdateType.FixedUpdate] == null)
            return;
        try
        {
            foreach (var d in _eventDic[E_UpdateType.FixedUpdate].GetInvocationList())
                (d as UnityAction)?.Invoke();
        }
        catch (System.Exception e)
        {

            Debug.LogError(e);
        }
    }
    public void AddUpdateListener(E_UpdateType type, UnityAction updateEvent) 
    {
        _eventDic[type] += updateEvent;
    }
    public void RemoveUpdateListener(E_UpdateType type, UnityAction updateEvent)
    {
        _eventDic[type] -= updateEvent;
    }

    public Coroutine StartCor(IEnumerator routine) 
    {
        return StartCoroutine(routine);
    }
    public void StopCor(Coroutine routine)
    {
        StopCoroutine(routine);
    }
    public void StopAllCor() 
    {
        StopAllCoroutines();
    }
}
