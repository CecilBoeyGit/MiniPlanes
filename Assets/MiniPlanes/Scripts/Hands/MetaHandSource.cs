using System.Collections.Generic;
using UnityEngine;

namespace MiniPlanes
{
    /// <summary>
    /// HandSource backed by the Meta XR Core SDK (OVRHand + OVRSkeleton). Works with both the
    /// OpenXR 26-joint skeleton (default for new projects) and the legacy OVR skeleton.
    /// </summary>
    [DefaultExecutionOrder(40)]
    public class MetaHandSource : HandSource
    {
        public OVRHand ovrHand;
        public OVRSkeleton skeleton;

        readonly Transform[] _joints = new Transform[(int)HandJoint.Count];
        bool _mapped;
        int _mappedBoneCount = -1;

        public override bool IsTracked =>
            _mapped && ovrHand != null && skeleton != null && ovrHand.IsTracked && skeleton.IsDataValid;

        public override bool IsHighConfidence => ovrHand != null && ovrHand.IsDataHighConfidence;

        public override float HandScale => ovrHand != null && ovrHand.HandScale > 0.05f ? ovrHand.HandScale : 1f;

        public override bool IsIndexPinching => ovrHand != null && ovrHand.GetFingerIsPinching(OVRHand.HandFinger.Index);

        // OpenXR skeleton: bone index == joint order below (palm first).
        static readonly HandJoint[] XrOrder =
        {
            HandJoint.Palm, HandJoint.Wrist,
            HandJoint.ThumbMetacarpal, HandJoint.ThumbProximal, HandJoint.ThumbDistal, HandJoint.ThumbTip,
            HandJoint.IndexMetacarpal, HandJoint.IndexProximal, HandJoint.IndexIntermediate, HandJoint.IndexDistal, HandJoint.IndexTip,
            HandJoint.MiddleMetacarpal, HandJoint.MiddleProximal, HandJoint.MiddleIntermediate, HandJoint.MiddleDistal, HandJoint.MiddleTip,
            HandJoint.RingMetacarpal, HandJoint.RingProximal, HandJoint.RingIntermediate, HandJoint.RingDistal, HandJoint.RingTip,
            HandJoint.LittleMetacarpal, HandJoint.LittleProximal, HandJoint.LittleIntermediate, HandJoint.LittleDistal, HandJoint.LittleTip,
        };

        static Dictionary<int, HandJoint> _legacy;

        static Dictionary<int, HandJoint> Legacy
        {
            get
            {
                if (_legacy != null) return _legacy;
                _legacy = new Dictionary<int, HandJoint>
                {
                    { (int)OVRSkeleton.BoneId.Hand_WristRoot, HandJoint.Wrist },
                    { (int)OVRSkeleton.BoneId.Hand_Thumb1, HandJoint.ThumbMetacarpal },
                    { (int)OVRSkeleton.BoneId.Hand_Thumb2, HandJoint.ThumbProximal },
                    { (int)OVRSkeleton.BoneId.Hand_Thumb3, HandJoint.ThumbDistal },
                    { (int)OVRSkeleton.BoneId.Hand_ThumbTip, HandJoint.ThumbTip },
                    { (int)OVRSkeleton.BoneId.Hand_Index1, HandJoint.IndexProximal },
                    { (int)OVRSkeleton.BoneId.Hand_Index2, HandJoint.IndexIntermediate },
                    { (int)OVRSkeleton.BoneId.Hand_Index3, HandJoint.IndexDistal },
                    { (int)OVRSkeleton.BoneId.Hand_IndexTip, HandJoint.IndexTip },
                    { (int)OVRSkeleton.BoneId.Hand_Middle1, HandJoint.MiddleProximal },
                    { (int)OVRSkeleton.BoneId.Hand_Middle2, HandJoint.MiddleIntermediate },
                    { (int)OVRSkeleton.BoneId.Hand_Middle3, HandJoint.MiddleDistal },
                    { (int)OVRSkeleton.BoneId.Hand_MiddleTip, HandJoint.MiddleTip },
                    { (int)OVRSkeleton.BoneId.Hand_Ring1, HandJoint.RingProximal },
                    { (int)OVRSkeleton.BoneId.Hand_Ring2, HandJoint.RingIntermediate },
                    { (int)OVRSkeleton.BoneId.Hand_Ring3, HandJoint.RingDistal },
                    { (int)OVRSkeleton.BoneId.Hand_RingTip, HandJoint.RingTip },
                    { (int)OVRSkeleton.BoneId.Hand_Pinky0, HandJoint.LittleMetacarpal },
                    { (int)OVRSkeleton.BoneId.Hand_Pinky1, HandJoint.LittleProximal },
                    { (int)OVRSkeleton.BoneId.Hand_Pinky2, HandJoint.LittleIntermediate },
                    { (int)OVRSkeleton.BoneId.Hand_Pinky3, HandJoint.LittleDistal },
                    { (int)OVRSkeleton.BoneId.Hand_PinkyTip, HandJoint.LittleTip },
                };
                return _legacy;
            }
        }

        void Awake()
        {
            if (ovrHand == null) ovrHand = GetComponent<OVRHand>();
            if (skeleton == null) skeleton = GetComponent<OVRSkeleton>();
        }

        void Update()
        {
            if (skeleton == null || !skeleton.IsInitialized) return;
            var bones = skeleton.Bones;
            if (bones == null || bones.Count == 0) return;
            if (!_mapped || bones.Count != _mappedBoneCount) Map(bones);
        }

        void Map(IList<OVRBone> bones)
        {
            for (int i = 0; i < _joints.Length; i++) _joints[i] = null;

            var type = skeleton.GetSkeletonType();
            bool openXr = type == OVRSkeleton.SkeletonType.XRHandLeft || type == OVRSkeleton.SkeletonType.XRHandRight;

            foreach (var bone in bones)
            {
                if (bone == null || bone.Transform == null) continue;
                int id = (int)bone.Id;
                if (openXr)
                {
                    if (id >= 0 && id < XrOrder.Length) _joints[(int)XrOrder[id]] = bone.Transform;
                }
                else if (Legacy.TryGetValue(id, out var joint))
                {
                    _joints[(int)joint] = bone.Transform;
                }
            }

            _mappedBoneCount = bones.Count;
            _mapped = _joints[(int)HandJoint.Wrist] != null
                      && _joints[(int)HandJoint.ThumbTip] != null
                      && _joints[(int)HandJoint.IndexTip] != null;
        }

        public override bool TryGetJoint(HandJoint joint, out Vector3 position)
        {
            var t = _joints[(int)joint];
            if (t != null)
            {
                position = t.position;
                return true;
            }

            // Legacy skeleton has no palm or finger metacarpals: synthesise the ones we need.
            switch (joint)
            {
                case HandJoint.Palm:
                    if (TryGetJoint(HandJoint.Wrist, out var w) && TryGetJoint(HandJoint.MiddleProximal, out var m))
                    {
                        position = Vector3.Lerp(w, m, 0.55f);
                        return true;
                    }
                    break;
                case HandJoint.IndexMetacarpal:
                case HandJoint.MiddleMetacarpal:
                case HandJoint.RingMetacarpal:
                    if (TryGetJoint(HandJoint.Wrist, out var wrist))
                    {
                        position = wrist;
                        return true;
                    }
                    break;
            }

            position = default;
            return false;
        }
    }
}
