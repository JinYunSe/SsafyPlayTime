using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DeathZoneRoot : MonoBehaviour
{
    [Header("Height Settings")]
    public float heightStep = 0.5f;
    public float heightInterval = 5.0f;
    public float maxHeight = 3.5f;
    float nextHeightTime;

    void Update()
    {
        if (transform.localScale.y < maxHeight)
        {
            if (Time.time >= nextHeightTime)
            {
                transform.localScale += new Vector3(0, heightStep, 0);

                nextHeightTime = Time.time + heightInterval;
            }
        }
    }
}
