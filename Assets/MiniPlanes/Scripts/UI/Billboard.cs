using UnityEngine;

namespace MiniPlanes
{
    /// <summary>Turns a small world-space UI element, such as a balloon icon, to face the player.</summary>
    public class Billboard : MonoBehaviour
    {
        /// <summary>The player's head. Set once by the game manager.</summary>
        public static Transform Head;

        void LateUpdate()
        {
            if (Head == null) return;
            Vector3 look = transform.position - Head.position;
            if (look.sqrMagnitude > 1e-8f) transform.rotation = Quaternion.LookRotation(look, Vector3.up);
        }
    }
}
