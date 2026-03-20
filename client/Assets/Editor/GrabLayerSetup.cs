using UnityEditor;
using UnityEngine;

public static class GrabLayerSetup
{
    [MenuItem("Tools/Setup Grab Layers")]
    public static void SetupGrabLayers()
    {
        int grabHurtbox = LayerMask.NameToLayer("GrabHurtbox");
        int grabSensor = LayerMask.NameToLayer("GrabSensor");

        if (grabHurtbox < 0 || grabSensor < 0)
        {
            Debug.LogError("[GrabLayerSetup] GrabHurtbox or GrabSensor layer not found!");
            return;
        }

        // GrabHurtbox: 다른 모든 레이어와 충돌 비활성화, GrabSensor만 활성화
        for (int i = 0; i < 32; i++)
        {
            if (i == grabSensor)
                Physics.IgnoreLayerCollision(grabHurtbox, i, false); // 충돌 활성화
            else
                Physics.IgnoreLayerCollision(grabHurtbox, i, true);  // 충돌 비활성화
        }

        // GrabSensor: 다른 모든 레이어와 충돌 비활성화, GrabHurtbox만 활성화
        for (int i = 0; i < 32; i++)
        {
            if (i == grabHurtbox)
                Physics.IgnoreLayerCollision(grabSensor, i, false);
            else
                Physics.IgnoreLayerCollision(grabSensor, i, true);
        }

        Debug.Log($"[GrabLayerSetup] Layer collision matrix configured: GrabHurtbox({grabHurtbox}) <-> GrabSensor({grabSensor}) only");
    }
}
