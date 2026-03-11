using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Trampoline : MonoBehaviour
{
    public float bounceForce = 20.0f;

    /*private void OnCollisionEnter(Collision collision)
    {
        // 1. 충돌 지점 정보 중 첫 번째 정보를 가져옵니다.
        ContactPoint contact = collision.contacts[0];

        // 2. 법선 벡터(Normal) 확인 
        // contact.normal은 충돌면이 바라보는 방향입니다.
        // 플레이어가 위에서 아래로 밟았다면, 충돌면의 법선은 위(Vector3.up)를 향합니다.
        // 하지만 유니티 물리 연산에서 상대 물체의 방향에 따라 값이 다를 수 있으므로
        // y값이 양수(위쪽 방향)인지 확인하는 것이 가장 확실합니다.
        if (contact.normal.y < -0.5f)
        {
            Rigidbody rb = collision.gameObject.GetComponent<Rigidbody>();

            if (rb != null)
            {
                // 아래로 떨어지던 속도를 초기화하여 팅겨나가는 힘을 일정하게 유지
                Vector3 vel = rb.velocity;
                vel.y = 0;
                rb.velocity = vel;

                // 위로 쏘아 올리기
                rb.AddForce(Vector3.up * bounceForce, ForceMode.Impulse);
            }
        }
    }*/

    /*private void OnCollisionEnter(Collision collision)
    {
        // 1. 위에서 밟았는지 법선 벡터 확인
        ContactPoint contact = collision.contacts[0];
        if (contact.normal.y < -0.5f)
        {
            // 2. 충돌한 오브젝트(팔, 다리 등)의 '부모' 계층에서 Rigidbody를 찾음
            // 최상단에 하나만 있다면 GetComponentInParent가 가장 효율적입니다.
            Rigidbody rootRb = collision.gameObject.GetComponentInParent<Rigidbody>();

            if (rootRb != null)
            {
                // 3. 속도 초기화 (캐릭터 전체의 물리력을 초기화)
                rootRb.velocity = new Vector3(rootRb.velocity.x, 0, rootRb.velocity.z);

                // 4. 최상단 리지드바디에 직접 위로 튕기는 힘 적용
                rootRb.AddForce(Vector3.up * bounceForce, ForceMode.Impulse);

                Debug.Log($"{collision.gameObject.name}이 닿았지만, {rootRb.gameObject.name}에 힘을 주었습니다!");
            }
        }
    }*/

    private void OnCollisionEnter(Collision collision)
    {
        // 1. 충돌한 물체의 최상위 부모(root)를 찾습니다.
        GameObject rootObj = collision.transform.root.gameObject;

        // 2. 최상위 부모가 "Player" 태그를 가지고 있는지 확인합니다.
        if (rootObj.CompareTag("Player"))
        {
            // 3. 위에서 밟았는지 확인 (법선 벡터)
            ContactPoint contact = collision.contacts[0];

            // 주의: 내(트램펄린) 입장에서 위쪽면이 부딪힌 것이므로 
            // contact.normal.y > 0.5f 가 일반적인 '위에서 밟음' 판정입니다.
            if (contact.normal.y < -0.5f)
            {
                // 4. 최상위 부모에 있는 Rigidbody를 가져와서 힘을 줍니다.
                Rigidbody rootRb = rootObj.GetComponent<Rigidbody>();

                if (rootRb != null)
                {
                    rootRb.velocity = new Vector3(rootRb.velocity.x, 0, rootRb.velocity.z);
                    rootRb.AddForce(Vector3.up * bounceForce, ForceMode.Impulse);

                    Debug.Log($"플레이어({rootObj.name}) 전체를 튕겨 올렸습니다!");
                }
            }
        }
    }
}
