using UnityEngine;

namespace SSAFYPlayTime.Character
{
    public sealed class GroundProbe
    {
        private readonly RaycastHit[] _hits = new RaycastHit[10];

        public bool IsGrounded(Vector3 position, Transform root, float radius, float distance)
        {
            var count = Physics.SphereCastNonAlloc(position, radius, Vector3.down, _hits, distance);
            for (var i = 0; i < count; i++)
            {
                if (_hits[i].transform.root == root)
                    continue;
                return true;
            }

            return false;
        }
    }
}
