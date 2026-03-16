using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class IgnoreCollision : MonoBehaviour
{
    [SerializeField]
    Collider thisCollider;

    [SerializeField]
    Collider[] colliderToIgnore;

    void Start()
    {
        if (thisCollider == null)
            thisCollider = GetComponent<Collider>();

        if (thisCollider == null || colliderToIgnore == null || colliderToIgnore.Length == 0)
            return;

        foreach (Collider otherCollider in colliderToIgnore)
        {
            if (otherCollider == null || otherCollider == thisCollider)
                continue;

            Physics.IgnoreCollision(thisCollider, otherCollider, true);
        }
    }
}
