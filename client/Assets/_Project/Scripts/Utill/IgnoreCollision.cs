using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class IgnoreCollision : MonoBehaviour
{
    [SerializeField]
    Collider thisCollider;

    [SerializeField]
    Collider[] colliderToIgnore;

    [SerializeField]
    bool ignoreAllChildColliders = true;

    void Start()
    {
        if (thisCollider == null)
            thisCollider = GetComponent<Collider>();

        // 수동 등록된 콜라이더 무시
        if (colliderToIgnore != null)
        {
            foreach (Collider otherCollider in colliderToIgnore)
            {
                if (otherCollider != null && otherCollider != thisCollider)
                    Physics.IgnoreCollision(thisCollider, otherCollider, true);
            }
        }

        // 자식 콜라이더 전부 자동 무시 (래그돌 본 충돌 방지)
        if (ignoreAllChildColliders)
        {
            Collider[] childColliders = GetComponentsInChildren<Collider>(true);
            foreach (Collider childCollider in childColliders)
            {
                if (childCollider != null && childCollider != thisCollider)
                    Physics.IgnoreCollision(thisCollider, childCollider, true);
            }
        }
    }
}
