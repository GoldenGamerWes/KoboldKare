using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;
#if UNITY_EDITOR
using UnityEditor;

public static class RagdollColliders {
    public class HumanoidRagdollColliders : List<RagdollCollider> {
    }

    public class RagdollCollider {
        public Transform GetParentBone(Animator animator) {
            return animator.transform.Find(parentPath);
        }
        public string parentPath;
        public Matrix4x4 localTransform;
        public string name;

        public RagdollCollider(string name) {
            this.name = name;
        }

        public virtual void DrawHandles(Animator animator) {
        }

        public virtual Collider GetOrCreate(Animator animator) {
            return null;
        }
    }
    

    public class RagdollCapsuleCollider : RagdollCollider {
        public enum CapsuleDirection {
            XAxis,
            YAxis,
            ZAxis,
        }

        public float height;
        public float radius;
        public Vector3 center = Vector3.zero;
        public CapsuleDirection capsuleDirection;

        public override Collider GetOrCreate(Animator animator) {
            GameObject childObject = null;
            var parent = GetParentBone(animator);
            if (parent.Find("RagdollCapsuleCollider") == null) {
                childObject = new GameObject(name+"RagdollCapsuleCollider", typeof(CapsuleCollider));
                childObject.transform.SetParent(parent);
                Undo.RegisterCreatedObjectUndo(childObject, "Created a capsule.");
            } else {
                childObject = parent.Find(name+"RagdollCapsuleCollider").gameObject;
                Undo.RegisterCompleteObjectUndo(childObject, "Changing the capsule.");
            }

            Undo.RegisterFullObjectHierarchyUndo(childObject.transform, "Moved child object");
            childObject.transform.localPosition = localTransform.GetPosition();
            childObject.transform.localRotation = localTransform.rotation;
            
            CapsuleCollider collider = childObject.GetComponent<CapsuleCollider>();
            Undo.RecordObject(collider, "Capsule changes");
            collider.center = center;
            collider.direction = (int)capsuleDirection;
            collider.radius = radius;
            collider.height = height+radius*2f;
            return collider;
        }

        public RagdollCapsuleCollider(Animator animator, string name, Matrix4x4 localTransform, Transform parent, Vector3 pointA, Vector3 pointB,
            float radius) : base(name) {
            Matrix4x4 localToWorld = parent.localToWorldMatrix * localTransform;
            Matrix4x4 worldToLocal = Matrix4x4.Inverse(localToWorld);
            Vector3 localPointA = worldToLocal.MultiplyPoint(pointA);
            Vector3 localPointB = worldToLocal.MultiplyPoint(pointB);
            center = (localPointA + localPointB) * 0.5f;
            this.radius = radius;
            height = Vector3.Distance(localPointA, localPointB);
            this.localTransform = localTransform;
            this.parentPath = AnimationUtility.CalculateTransformPath(parent, animator.transform);
            Vector3 localDiff = localPointB - localPointA;
            capsuleDirection = CapsuleDirection.YAxis;
            if (Mathf.Abs(localDiff.x) > Mathf.Abs(localDiff.y) && Mathf.Abs(localDiff.x) > Mathf.Abs(localDiff.z)) {
                capsuleDirection = CapsuleDirection.XAxis;
            } else if (Mathf.Abs(localDiff.z) > Mathf.Abs(localDiff.x) &&
                       Mathf.Abs(localDiff.z) > Mathf.Abs(localDiff.y)) {
                capsuleDirection = CapsuleDirection.ZAxis;
            }
        }

        public override void DrawHandles(Animator animator) {
            var parent = GetParentBone(animator);
            Matrix4x4 localToWorld = parent.localToWorldMatrix * localTransform;
            Vector3 direction = Vector3.zero;
            Matrix4x4 rotation = Matrix4x4.identity;
            switch (capsuleDirection) {
                case CapsuleDirection.XAxis:
                    direction = Vector3.right;
                    rotation = Matrix4x4.Rotate(Quaternion.AngleAxis(90f,
                        (localTransform * parent.localToWorldMatrix * Vector3.forward).normalized));
                    break;
                case CapsuleDirection.YAxis:
                    direction = Vector3.up;
                    break;
                case CapsuleDirection.ZAxis:
                    direction = Vector3.forward;
                    rotation = Matrix4x4.Rotate(Quaternion.AngleAxis(-90f,
                        (localTransform * parent.localToWorldMatrix * Vector3.right).normalized));
                    break;
            }

            Vector3 localPointA = center + direction * this.height * 0.5f;
            Vector3 localPointB = center - direction * this.height * 0.5f;
            Vector3 localDiff = localPointB - localPointA;
            Vector3 lengthWise = Vector3.up;
            var height = localDiff.magnitude;
            center = Matrix4x4.Inverse(rotation) * ((localPointA + localPointB) * 0.5f);
            using var scope = new Handles.DrawingScope(localToWorld * rotation);
            DrawWireCapsule(center + lengthWise * height * 0.5f, center - lengthWise * height * 0.5f, radius);
        }

        private static void DrawWireCapsule(Vector3 upper, Vector3 lower, float radius) {
            var offsetX = new Vector3(radius, 0f, 0f);
            var offsetZ = new Vector3(0f, 0f, radius);
            Handles.DrawWireArc(upper, Vector3.back, Vector3.left, 180, radius);
            Handles.DrawLine(lower + offsetX, upper + offsetX);
            Handles.DrawLine(lower - offsetX, upper - offsetX);
            Handles.DrawWireArc(lower, Vector3.back, Vector3.left, -180, radius);
            Handles.DrawWireArc(upper, Vector3.left, Vector3.back, -180, radius);
            Handles.DrawLine(lower + offsetZ, upper + offsetZ);
            Handles.DrawLine(lower - offsetZ, upper - offsetZ);
            Handles.DrawWireArc(lower, Vector3.left, Vector3.back, 180, radius);
            Handles.DrawWireDisc(upper, Vector3.up, radius);
            Handles.DrawWireDisc(lower, Vector3.up, radius);
        }
    }

    public class RagdollBoxCollider : RagdollCollider {
        public RagdollBoxCollider(Animator animator, string name, Matrix4x4 localTransform, Transform parent, Vector3 pointA, Vector3 pointB,
            Vector3 connectA, Vector3 connectB, float depth, float depthOffset) : base(name) {
            Matrix4x4 localToWorld = parent.localToWorldMatrix * localTransform;
            var worldToLocal = Matrix4x4.Inverse(localToWorld);
            Vector3 localPointA = worldToLocal.MultiplyPoint(pointA);
            Vector3 localPointB = worldToLocal.MultiplyPoint(pointB);
            Vector3 localConnectPointA = worldToLocal.MultiplyPoint(connectA);
            Vector3 localConnectPointB = worldToLocal.MultiplyPoint(connectB);

            Vector3 localDiffA = localPointB - localPointA;
            Vector3 localDiffB = localConnectPointB - localConnectPointA;

            Vector3 depthAdjust = Vector3.Cross(localDiffA.normalized, localDiffB.normalized) * depth;

            center = (localPointA + localPointB) * 0.5f;
            size = Vector3.zero;
            size += new Vector3(Mathf.Abs(localDiffA.x), Mathf.Abs(localDiffA.y), Mathf.Abs(localDiffA.z));
            size += new Vector3(Mathf.Abs(localDiffB.x), Mathf.Abs(localDiffB.y), Mathf.Abs(localDiffB.z));
            size += new Vector3(Mathf.Abs(depthAdjust.x), Mathf.Abs(depthAdjust.y), Mathf.Abs(depthAdjust.z));
            center -= depthOffset * depthAdjust;
            this.parentPath = AnimationUtility.CalculateTransformPath(parent, animator.transform);
            this.localTransform = localTransform;
        }

        public Vector3 size;
        public Vector3 center;
        public Vector3 extents => size * 0.5f;
        public override Collider GetOrCreate(Animator animator) {
            var parent = GetParentBone(animator);
            GameObject childObject = null;
            if (parent.Find("RagdollBoxCollider") == null) {
                childObject = new GameObject(name+"RagdollBoxCollider", typeof(BoxCollider));
                childObject.transform.SetParent(parent);
                Undo.RegisterCreatedObjectUndo(childObject, "Created box collider");
            } else {
                childObject = parent.Find(name+"RagdollBoxCollider").gameObject;
                Undo.RegisterCompleteObjectUndo(childObject, "Changed box collider");
            }
            Undo.RegisterFullObjectHierarchyUndo(childObject, "Moved box collider");
            childObject.transform.localPosition = localTransform.GetPosition();
            childObject.transform.localRotation = localTransform.rotation;
            
            BoxCollider collider = childObject.GetComponent<BoxCollider>();
            Undo.RecordObject(collider, "Changed box collider's info");
            collider.center = center;
            collider.size = size;
            return collider;
        }

        public override void DrawHandles(Animator animator) {
            var parent = GetParentBone(animator);
            Matrix4x4 localToWorld = parent.localToWorldMatrix * localTransform;
            Matrix4x4 worldToLocal = Matrix4x4.Inverse(localToWorld);
            using var scope = new Handles.DrawingScope(localToWorld);
            Handles.DrawWireCube(center, size);
        }
    }
    public static HumanoidRagdollColliders GenerateColliders(Animator animator,
        RagdollColliderConfiguration colliderConfiguration, RagdollScrunchStretchPack cachedScrunchStretchPack) {
        var newGameObject = Object.Instantiate(animator.gameObject);
        animator = newGameObject.GetComponent<Animator>();
        cachedScrunchStretchPack.GetNeutralClip().SampleAnimation(animator.gameObject, 0f);
        HumanoidRagdollColliders colliders = new HumanoidRagdollColliders();
        var head = animator.GetBoneTransform(HumanBodyBones.Head);
        var hip = animator.GetBoneTransform(HumanBodyBones.Hips);
        var neck = animator.GetBoneTransform(HumanBodyBones.Neck);
        var chest = animator.GetBoneTransform(HumanBodyBones.Chest);

        // Left arm
        var leftUpperArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        var leftLowerArm = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        var leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        var rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);

        colliders.Add(new RagdollCapsuleCollider(animator, "leftUpperArm", Matrix4x4.identity, leftUpperArm, leftUpperArm.position,
            leftLowerArm.position, colliderConfiguration.upperArmRadius));
        colliders.Add(new RagdollCapsuleCollider(animator, "leftLowerArm", Matrix4x4.identity, leftLowerArm, leftLowerArm.position,
            leftHand.position, colliderConfiguration.lowerArmRadius));
        // Left hand
        Vector3 leftHandForward = (leftHand.position - leftLowerArm.position).normalized;
        Vector3 leftHandRight = (leftLowerArm.position - leftUpperArm.position).normalized;
        Vector3 leftHandUp = Vector3.Cross(leftHandForward, leftHandRight);
        Vector3.OrthoNormalize(ref leftHandForward, ref leftHandUp, ref leftHandRight);
        Matrix4x4 leftHandChild = Matrix4x4.Rotate(Quaternion.LookRotation(
            leftHand.transform.InverseTransformDirection(leftHandForward),
            leftHand.transform.InverseTransformDirection(leftHandUp)));
        colliders.Add(new RagdollBoxCollider(animator, "leftHand", leftHandChild, leftHand, leftHand.position,
            leftHand.position + leftHandForward * colliderConfiguration.handLength,
            leftHand.position + leftHandRight * colliderConfiguration.lowerArmRadius * 2f,
            leftHand.position - leftHandRight * colliderConfiguration.lowerArmRadius * 2f, colliderConfiguration.lowerArmRadius * 2f,
            0));

        // Right arm
        var rightUpperArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        var rightLowerArm = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
        colliders.Add(new RagdollCapsuleCollider(animator, "rightUpperArm", Matrix4x4.identity, rightUpperArm, rightUpperArm.position,
            rightLowerArm.position, colliderConfiguration.upperArmRadius));
        colliders.Add(new RagdollCapsuleCollider(animator, "rightLowerArm", Matrix4x4.identity, rightLowerArm, rightLowerArm.position,
            rightHand.position, colliderConfiguration.lowerArmRadius));

        // Right hand
        Vector3 rightHandForward = (rightHand.position - rightLowerArm.position).normalized;
        Vector3 rightHandRight = (rightLowerArm.position - rightUpperArm.position).normalized;
        Vector3 rightHandUp = Vector3.Cross(rightHandForward, rightHandRight);
        Vector3.OrthoNormalize(ref rightHandForward, ref rightHandUp, ref rightHandRight);
        Matrix4x4 rightHandChild = Matrix4x4.Rotate(Quaternion.LookRotation(
            rightHand.transform.InverseTransformDirection(rightHandForward),
            rightHand.transform.InverseTransformDirection(rightHandUp)));
        colliders.Add(new RagdollBoxCollider(animator, "rightHand", rightHandChild, rightHand, rightHand.position,
            rightHand.position + rightHandForward * colliderConfiguration.handLength,
            rightHand.position + rightHandRight * colliderConfiguration.lowerArmRadius * 2f,
            rightHand.position - rightHandRight * colliderConfiguration.lowerArmRadius * 2f, colliderConfiguration.lowerArmRadius * 2f,
            0));

        var leftUpperLeg = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
        var leftLowerLeg = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
        var leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        var rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);

        // Left foot
        Vector3 leftFootRight = (rightFoot.position - leftFoot.position).normalized;
        Vector3 leftFootUp = (leftLowerLeg.position - leftFoot.position).normalized;
        Vector3 leftFootForward = Vector3.Cross(leftFootRight, leftFootUp);
        Vector3.OrthoNormalize(ref leftFootForward, ref leftFootUp, ref leftFootRight);
        Matrix4x4 leftFootChild = Matrix4x4.Rotate(Quaternion.Inverse(leftFoot.rotation) *
                                                   Quaternion.LookRotation(leftFootForward, leftFootUp));
        colliders.Add(new RagdollBoxCollider(animator, "leftFoot", leftFootChild, leftFoot,
            leftFoot.position + leftFootUp * colliderConfiguration.lowerLegRadius * 0.75f,
            leftFoot.position - leftFootUp * colliderConfiguration.lowerLegRadius * 0.75f,
            leftFoot.position + leftFootRight * colliderConfiguration.lowerLegRadius,
            leftFoot.position - leftFootRight * colliderConfiguration.lowerLegRadius, colliderConfiguration.footLength,
            colliderConfiguration.footOffset));

        // Left leg
        colliders.Add(new RagdollCapsuleCollider(animator, "leftUpperLeg", Matrix4x4.identity, leftUpperLeg, leftUpperLeg.position,
            leftLowerLeg.position, colliderConfiguration.upperLegRadius));
        if (!colliderConfiguration.digitigradeLegs) {
            colliders.Add(new RagdollCapsuleCollider(animator, "leftLowerLeg", Matrix4x4.identity, leftLowerLeg, leftLowerLeg.position,
                leftFoot.position, colliderConfiguration.lowerLegRadius));
        } else {
            float lowerLegLength = Vector3.Distance(leftLowerLeg.position, leftFoot.position);
            Vector3 leftDigitigradeTarget = leftFoot.position -
                                            leftFootForward * colliderConfiguration.digitigradePushBack * lowerLegLength +
                                            leftFootUp * colliderConfiguration.digitigradePushUp * lowerLegLength;
            Vector3 leftUpperDigitgradeRight = (rightFoot.position - leftFoot.position).normalized;
            Vector3 leftUpperDigitgradeUp = (leftLowerLeg.position - leftDigitigradeTarget).normalized;
            Vector3 leftUpperDigitgradeForward = Vector3.Cross(leftUpperDigitgradeRight, leftUpperDigitgradeUp);
            Vector3.OrthoNormalize(ref leftUpperDigitgradeForward, ref leftUpperDigitgradeUp,
                ref leftUpperDigitgradeRight);
            Matrix4x4 leftUpperDigitgradeChild = Matrix4x4.Rotate(Quaternion.Inverse(leftLowerLeg.rotation) *
                                                                  Quaternion.LookRotation(leftUpperDigitgradeForward,
                                                                      leftUpperDigitgradeUp));
            colliders.Add(new RagdollCapsuleCollider(animator, "leftLowerLeg", leftUpperDigitgradeChild, leftLowerLeg,
                leftLowerLeg.position, leftDigitigradeTarget, colliderConfiguration.lowerLegRadius));
            Vector3 leftLowerDigitgradeRight = (rightFoot.position - leftFoot.position).normalized;
            Vector3 leftLowerDigitgradeUp = (leftDigitigradeTarget - leftFoot.position).normalized;
            Vector3 leftLowerDigitgradeForward = Vector3.Cross(leftLowerDigitgradeRight, leftLowerDigitgradeUp);
            Vector3.OrthoNormalize(ref leftLowerDigitgradeForward, ref leftLowerDigitgradeUp,
                ref leftLowerDigitgradeRight);
            Matrix4x4 leftLowerDigitgradeChild = Matrix4x4.Rotate(Quaternion.Inverse(leftLowerLeg.rotation) *
                                                                  Quaternion.LookRotation(leftLowerDigitgradeForward,
                                                                      leftLowerDigitgradeUp));
            colliders.Add(new RagdollCapsuleCollider(animator, "leftLowerLegDigitigrade", leftLowerDigitgradeChild, leftLowerLeg,
                leftDigitigradeTarget, leftFoot.position, colliderConfiguration.lowerLegRadius));
        }

        var rightUpperLeg = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
        var rightLowerLeg = animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);

        // Right foot
        Vector3 rightFootRight = (rightFoot.position - leftFoot.position).normalized;
        Vector3 rightFootUp = (rightLowerLeg.position - rightFoot.position).normalized;
        Vector3 rightFootForward = Vector3.Cross(rightFootRight, rightFootUp);
        Vector3.OrthoNormalize(ref rightFootForward, ref rightFootUp, ref rightFootRight);
        Matrix4x4 rightFootChild = Matrix4x4.Rotate(Quaternion.Inverse(rightFoot.rotation) *
                                                    Quaternion.LookRotation(rightFootForward, rightFootUp));
        colliders.Add(new RagdollBoxCollider(animator, "rightFoot", rightFootChild, rightFoot,
            rightFoot.position + rightFootUp * colliderConfiguration.lowerLegRadius * 0.75f,
            rightFoot.position - rightFootUp * colliderConfiguration.lowerLegRadius * 0.75f,
            rightFoot.position + rightFootRight * colliderConfiguration.lowerLegRadius,
            rightFoot.position - rightFootRight * colliderConfiguration.lowerLegRadius, colliderConfiguration.footLength,
            colliderConfiguration.footOffset));

        // Right leg
        colliders.Add(new RagdollCapsuleCollider(animator, "rightUpperLeg", Matrix4x4.identity, rightUpperLeg, rightUpperLeg.position,
            rightLowerLeg.position, colliderConfiguration.upperLegRadius));
        if (!colliderConfiguration.digitigradeLegs) {
            colliders.Add(new RagdollCapsuleCollider(animator, "rightLowerLeg", Matrix4x4.identity, rightLowerLeg,
                rightLowerLeg.position, rightFoot.position, colliderConfiguration.lowerLegRadius));
        } else {
            float lowerLegLength = Vector3.Distance(rightLowerLeg.position, rightFoot.position);
            Vector3 rightDigitigradeTarget = rightFoot.position -
                                             rightFootForward * colliderConfiguration.digitigradePushBack * lowerLegLength +
                                             rightFootUp * colliderConfiguration.digitigradePushUp * lowerLegLength;
            Vector3 rightUpperDigitgradeRight = (rightFoot.position - leftFoot.position).normalized;
            Vector3 rightUpperDigitgradeUp = (rightLowerLeg.position - rightDigitigradeTarget).normalized;
            Vector3 rightUpperDigitgradeForward = Vector3.Cross(rightUpperDigitgradeRight, rightUpperDigitgradeUp);
            Vector3.OrthoNormalize(ref rightUpperDigitgradeForward, ref rightUpperDigitgradeUp,
                ref rightUpperDigitgradeRight);
            Matrix4x4 rightUpperDigitgradeChild = Matrix4x4.Rotate(Quaternion.Inverse(rightLowerLeg.rotation) *
                                                                   Quaternion.LookRotation(rightUpperDigitgradeForward,
                                                                       rightUpperDigitgradeUp));
            colliders.Add(new RagdollCapsuleCollider(animator, "rightLowerLeg", rightUpperDigitgradeChild, rightLowerLeg,
                rightLowerLeg.position, rightDigitigradeTarget, colliderConfiguration.lowerLegRadius));
            Vector3 rightLowerDigitgradeRight = (rightFoot.position - leftFoot.position).normalized;
            Vector3 rightLowerDigitgradeUp = (rightDigitigradeTarget - rightFoot.position).normalized;
            Vector3 rightLowerDigitgradeForward = Vector3.Cross(rightLowerDigitgradeRight, rightLowerDigitgradeUp);
            Vector3.OrthoNormalize(ref rightLowerDigitgradeForward, ref rightLowerDigitgradeUp,
                ref rightLowerDigitgradeRight);
            Matrix4x4 rightLowerDigitgradeChild = Matrix4x4.Rotate(Quaternion.Inverse(rightLowerLeg.rotation) *
                                                                   Quaternion.LookRotation(rightLowerDigitgradeForward,
                                                                       rightLowerDigitgradeUp));
            colliders.Add(new RagdollCapsuleCollider(animator, "rightLowerLegDigitigrade", rightLowerDigitgradeChild, rightLowerLeg,
                rightDigitigradeTarget, rightFoot.position, colliderConfiguration.lowerLegRadius));
        }

        // Chest
        Vector3 chestRight = (rightHand.position - leftHand.position).normalized;
        Vector3 chestUp = (neck.position - chest.position).normalized;
        Vector3.OrthoNormalize(ref chestRight, ref chestUp);
        Vector3 chestForward = Vector3.Cross(chestRight, chestUp);
        Matrix4x4 chestChild =
            Matrix4x4.Rotate(Quaternion.Inverse(chest.rotation) * Quaternion.LookRotation(chestForward, chestUp));
        colliders.Add(new RagdollBoxCollider(animator, "chest", chestChild, chest, chest.position,
            (leftUpperArm.position + rightUpperArm.position) * 0.5f + chestUp * colliderConfiguration.upperArmRadius * 0.5f,
            leftUpperArm.position, rightUpperArm.position, colliderConfiguration.chestDepth, colliderConfiguration.chestOffset));

        Vector3 hipVector = (rightUpperLeg.position - leftUpperLeg.position).normalized;
        Vector3 leftUpperLegAdjust = leftUpperLeg.position - hipVector * colliderConfiguration.upperLegRadius * 0.25f;
        Vector3 rightUpperLegAdjust = rightUpperLeg.position + hipVector * colliderConfiguration.upperLegRadius * 0.25f;

        // Spine
        var spine = animator.GetBoneTransform(HumanBodyBones.Spine);
        Vector3 spineRight = (rightHand.position - leftHand.position).normalized;
        Vector3 spineUp = (chest.position - spine.position).normalized;
        Vector3.OrthoNormalize(ref spineRight, ref spineUp);
        Vector3 spineForward = Vector3.Cross(spineRight, spineUp);
        Matrix4x4 spineChild =
            Matrix4x4.Rotate(Quaternion.Inverse(spine.rotation) * Quaternion.LookRotation(spineForward, spineUp));
        colliders.Add(new RagdollBoxCollider(animator, "spine", spineChild, spine, spine.position, chest.position,
            (leftUpperLegAdjust + leftUpperArm.position) * 0.5f, (rightUpperLegAdjust + rightUpperArm.position) * 0.5f,
            (colliderConfiguration.hipDepth + colliderConfiguration.chestDepth) * 0.5f,
            (colliderConfiguration.hipOffset + colliderConfiguration.chestOffset) * 0.5f));

        // Hip
        Vector3 legCenter = (leftUpperLeg.position + rightUpperLeg.position) * 0.5f;
        Vector3 fakeHipPosition = (hip.position + legCenter) * 0.5f;

        Vector3 hipRight = (rightHand.position - leftHand.position).normalized;
        Vector3 hipUp = (spine.position - fakeHipPosition).normalized;
        Vector3.OrthoNormalize(ref hipRight, ref hipUp);
        Vector3 hipForward = Vector3.Cross(hipRight, hipUp);
        Matrix4x4 hipChild =
            Matrix4x4.Rotate(Quaternion.Inverse(hip.rotation) * Quaternion.LookRotation(hipForward, hipUp));
        colliders.Add(new RagdollBoxCollider(animator, "hip", hipChild, hip, fakeHipPosition, spine.position,
            leftUpperLegAdjust, rightUpperLegAdjust, colliderConfiguration.hipDepth, colliderConfiguration.hipOffset));

        // Neck
        colliders.Add(new RagdollCapsuleCollider(animator, "neck", Matrix4x4.identity, neck, neck.position, head.position,
            colliderConfiguration.upperArmRadius));

        // Head
        Vector3 headRight = (rightHand.position - leftHand.position).normalized;
        Vector3 headUp = (head.position - neck.position).normalized;
        Vector3.OrthoNormalize(ref headRight, ref headUp);
        Vector3 headForward = Vector3.Cross(headRight, headUp);

        Matrix4x4 headChild =
            Matrix4x4.Rotate(Quaternion.Inverse(head.rotation) * Quaternion.LookRotation(headForward, headUp));

        colliders.Add(new RagdollBoxCollider(animator, "head", headChild, head, head.position,
            head.position + headUp * colliderConfiguration.headRadius * 2f,
            head.position - headRight * colliderConfiguration.headRadius, head.position + headRight * colliderConfiguration.headRadius,
            colliderConfiguration.muzzleLength * 2f, colliderConfiguration.muzzleOffset));

        // Tail
        if (colliderConfiguration.tailPath != null) {
            const int maxDepth = 16;
            Transform start = animator.transform.Find(colliderConfiguration.tailPath);
            int depth = 0;
            Transform end = start.GetChild(0);
            while (depth < maxDepth) {
                if (end.GetComponent<Collider>() != null) {
                    break;
                }
                depth++;
                if (end.childCount == 0) {
                    break;
                }

                end = end.GetChild(0);
            }

            int currentDepth = 0;
            end = start.GetChild(0);
            while (currentDepth <= depth) {
                if (end.GetComponent<Collider>() != null) {
                    start = start.parent;
                    end = end.parent;
                    break;
                }
                float tailRadiusSample = colliderConfiguration.tailRadiusCurve.Evaluate((float)currentDepth / (float)depth) *
                                         colliderConfiguration.tailRadiusMultiplier;
                colliders.Add(new RagdollCapsuleCollider(animator, $"tail{currentDepth}", Matrix4x4.identity, start.transform, start.position,
                    end.position, tailRadiusSample));
                currentDepth++;
                if (end.childCount == 0) {
                    break;
                }

                start = end;
                end = end.GetChild(0);
            }

            float lastSample = colliderConfiguration.tailRadiusCurve.Evaluate(1f) * colliderConfiguration.tailRadiusMultiplier;
            Vector3 forward = end.position - start.position;
            colliders.Add(new RagdollCapsuleCollider(animator, "tailEnd", Matrix4x4.identity, end.transform, end.position,
                end.position + forward, lastSample));
        }

        Object.DestroyImmediate(newGameObject);
        return colliders;
    }

    public static void PreviewColliders(Animator animator, HumanoidRagdollColliders colliders) {
        foreach (var collider in colliders) {
            collider.DrawHandles(animator);
        }
    }
}
#endif