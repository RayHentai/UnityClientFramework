using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class SingletonLazyBase<T> where T : class, new()
{
    private static readonly Lazy<T> lazy = new Lazy<T>(() => new T()); 
    public static T Instance => lazy.Value;
}
