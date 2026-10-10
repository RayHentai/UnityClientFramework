using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public abstract class SingletonBase<T> where T : class
{
    private static T instance;
    protected static readonly object lockObj = new object(); 
    public static T Instance 
    {
        get 
        {
            if (instance == null) 
            {
                lock (lockObj) 
                {
                    if (instance == null) 
                    {
                        Type type = typeof(T);
                        ConstructorInfo constructor = type.GetConstructor
                        (
                            BindingFlags.Instance | BindingFlags.NonPublic, //表示成员私有方法
                            null, //表示没有绑定对象
                            Type.EmptyTypes, //表示没有参数
                            null //表示没有参数修饰符
                        );
                        if (constructor != null)
                            instance = constructor.Invoke(null) as T;
                        else
                            Debug.LogError("没有找到私有无参构造函数");
                    }
                }
            }
            return instance;
        }
    }
}
