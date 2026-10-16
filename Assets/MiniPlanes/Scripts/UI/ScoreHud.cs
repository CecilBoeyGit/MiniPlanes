using UnityEngine;
using UnityEngine.UI;

namespace MiniPlanes
{
    /// <summary>World-space score panel that floats in front of the player and lazily follows their gaze.</summary>
    public class ScoreHud : MonoBehaviour
    {
        public Transform head;
        public Text statusText;
        public Text messageText;
        public float distance = 0.9f;
        public float heightOffset = -0.16f;
        public float followSpeed = 3f;

        float _messageUntil = float.PositiveInfinity;

        public void SetStatus(int score, int lives, string levelName)
        {
            if (statusText == null) return;
            statusText.text = "Score " + score + "    Lives " + lives + "    " + levelName;
        }

        public void SetMessage(string message, float seconds = 0f)
        {
            if (messageText == null) return;
            messageText.text = message;
            _messageUntil = seconds > 0f ? Time.time + seconds : float.PositiveInfinity;
        }

        public void ClearMessage()
        {
            if (messageText != null) messageText.text = string.Empty;
            _messageUntil = float.PositiveInfinity;
        }

        void LateUpdate()
        {
            if (Time.time > _messageUntil) ClearMessage();
            if (head == null) return;

            Vector3 fwd = head.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();

            Vector3 target = head.position + fwd * distance + Vector3.up * heightOffset;
            float k = 1f - Mathf.Exp(-followSpeed * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, target, k);
            Vector3 look = transform.position - head.position;
            if (look.sqrMagnitude > 1e-6f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(look, Vector3.up), k);
        }
    }
}
