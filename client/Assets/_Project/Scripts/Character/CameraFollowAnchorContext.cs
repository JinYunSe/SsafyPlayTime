using UnityEngine;

namespace SSAFYPlayTime.Character
{
    [DisallowMultipleComponent]
    public sealed class CameraFollowAnchorContext : MonoBehaviour
    {
        [SerializeField] private NetworkPlayer owner;

        public NetworkPlayer Owner => owner;

        public void SetOwner(NetworkPlayer networkPlayer)
        {
            owner = networkPlayer;
        }
    }
}
