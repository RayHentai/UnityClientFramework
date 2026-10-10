using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public class PoolManager : SingletonLazyBase<PoolManager>
{
    public class PoolGameObjData
    {
        public static bool isOpenLayout = true;
        private Stack<GameObject> _stack = new();
        private List<GameObject> _usedList = new();
        private GameObject _poolDateRoot;
        private int maxNum;
        public int StackCount => _stack.Count;
        public bool NeedCreate => _usedList.Count < maxNum;
        public PoolGameObjData(string name, GameObject gameObj, GameObject Root)
        {
            if (isOpenLayout)
            {
                _poolDateRoot = new(name);
                _poolDateRoot.transform.SetParent(Root.transform);
            }
            maxNum = gameObj.GetComponent<PoolObject>().maxNum;
            _usedList.Add(gameObj);
        }
        public GameObject Pop()
        {
            GameObject obj;
            if (StackCount > 0)
            {
                obj = _stack.Pop();
                _usedList.Add(obj);
            }
            else
            {
                obj = _usedList[0];
                _usedList.RemoveAt(0);
                _usedList.Add(obj);
            }
            obj.SetActive(true);
            if (isOpenLayout)
                obj.transform.SetParent(null);
            return obj;
        }
        public void Push(GameObject gameObj)
        {
            gameObj.SetActive(false);
            _usedList.Remove(gameObj);
            _stack.Push(gameObj);
            if (isOpenLayout)
                gameObj.transform.SetParent(_poolDateRoot.transform);
        }
        public void PushUsedList(GameObject gameObject)
        {
            _usedList.Add(gameObject);
        }
        public void ClearAllGameObj()
        {
            while (StackCount > 0)
                GameObject.Destroy(_stack.Pop());
            for (int i = 0; i < _usedList.Count; i++)
                GameObject.Destroy(_usedList[i]);
            _stack.Clear();
            _usedList.Clear();
        }
    }

    protected interface IPoolObjData { }
    public class PoolObjData<T> : IPoolObjData where T : class, new()
    {
        public Queue<T> queue = new();
    }

    private Dictionary<string, PoolGameObjData> _gameObjDic = new();
    private Dictionary<string, IPoolObjData> _objDic = new();
    private GameObject _poolMgrRoot;

    public GameObject GetGameObj(string name)
    {
        if (_poolMgrRoot == null && PoolGameObjData.isOpenLayout)
            _poolMgrRoot = new("Pool");
        GameObject gameObj;
        if (!_gameObjDic.ContainsKey(name) || _gameObjDic[name].StackCount == 0 && _gameObjDic[name].NeedCreate)
        {
            gameObj = GameObject.Instantiate(Resources.Load<GameObject>(name));
            gameObj.name = name;
            if (!_gameObjDic.ContainsKey(name))
                _gameObjDic.Add(name, new PoolGameObjData(name, gameObj, _poolMgrRoot));
            else
                _gameObjDic[name].PushUsedList(gameObj);
        }
        else
            gameObj = _gameObjDic[name].Pop();
        return gameObj;
    }

    public void PushGameObj(GameObject gameObj)
    {
        if (_poolMgrRoot == null && PoolGameObjData.isOpenLayout)
            _poolMgrRoot = new("Pool");
        if (_gameObjDic.ContainsKey(gameObj.name))
            _gameObjDic[gameObj.name].Push(gameObj);
        else
            Debug.LogError("传入正确的对象");
    }

    public T GetObj<T>(string nameSpace = "") where T : class, new()
    {
        string name = nameSpace + typeof(T).Name;
        if (!_objDic.ContainsKey(name) || (_objDic[name] as PoolObjData<T>).queue.Count == 0)
            return new T();
        else
            return (_objDic[name] as PoolObjData<T>).queue.Dequeue();
    }
    public void PushObj<T>(T obj, string nameSpace = "") where T : class, new()
    {
        string name = nameSpace + typeof(T).Name;
        if (_objDic.ContainsKey(name))
            (_objDic[name] as PoolObjData<T>).queue.Enqueue(obj);
        else
            Debug.LogError("传入正确的对象");
    }
    public void Clear(bool destroyObj, bool isClearObject = false) 
    {
        if (destroyObj)
        {
            if (PoolGameObjData.isOpenLayout)
                GameObject.Destroy(_poolMgrRoot);
            else 
                foreach (var key in _gameObjDic.Keys)
                    _gameObjDic[key].ClearAllGameObj();
        }
        _gameObjDic.Clear();
        _poolMgrRoot = null;
        if (isClearObject)
            _objDic.Clear();
    }
}
