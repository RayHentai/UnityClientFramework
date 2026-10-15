using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ResourcesManagerTest : MonoBehaviour
{
    [SerializeField]
    private GameObject _obj;
    void Start()
    {
        print(ResourcesManager.Instance.Load<GameObject>("ResTest/Cube"));
        print(ResourcesManager.Instance.Load<GameObject>("ResTest/Cube1"));
        print(ResourcesManager.Instance.Load("ResTest/Cube", typeof(GameObject)));
        print(ResourcesManager.Instance.Load("ResTest/Cube1", typeof(GameObject)));

        ResourcesManager.Instance.LoadAsync<GameObject>("ResTest/Cube", (asset) => { print("执行异步回调" + asset); });
        ResourcesManager.Instance.LoadAsync<GameObject>("ResTest/Cube1", (asset) => { print("执行异步回调" + asset); });
        ResourcesManager.Instance.LoadAsync<GameObject>("ResTest/Cube");

        ResourcesManager.Instance.LoadAsync("ResTest/Cube", typeof(GameObject), (asset) => { print("执行异步回调" + asset); });
        ResourcesManager.Instance.LoadAsync("ResTest/Cube1", typeof(GameObject), (asset) => { print("执行异步回调(Type)" + asset); });

        ResourcesManager.Instance.LoadAsync("ResTest/Item", typeof(Sprite), (asset) => { print("执行异步回调(Type)" + asset); });
        ResourcesManager.Instance.UnloadAsset(ResourcesManager.Instance.Load<AudioClip>("ResTest/1"));
        ResourcesManager.Instance.UnloadAsset(ResourcesManager.Instance.Load<GameObject>("ResTest/Cube"));
        ResourcesManager.Instance.UnloadUnusedAssets(() => { print("执行卸载回调"); });
    }
}
