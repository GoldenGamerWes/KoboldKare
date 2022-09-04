﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing.Text;
using System.IO;
using UnityEngine;
using Photon.Pun;
using System.Linq;
using KoboldKare;

public class PrecisionGrabber : MonoBehaviourPun, IPunObservable, ISavable {
    [SerializeField] private GameObject handDisplayPrefab;
    [SerializeField] private Transform view;
    [SerializeField] private GameObject freezeVFXPrefab;
    [SerializeField] private AudioPack unfreezeSound;
    [SerializeField] private Kobold kobold;
    [SerializeField] private GameEventFloat handVisibilityEvent;
    [SerializeField] private GameObject freezeUI;
    [SerializeField] private List<GameObject> activeUI;
    
    private static RaycastHit[] hits = new RaycastHit[10];
    private static readonly int GrabbingHash = Animator.StringToHash("Grabbing");
    private static readonly int BrightnessContrastSaturation = Shader.PropertyToID("_HueBrightnessContrastSaturation");
    private RaycastHitDistanceComparer raycastHitDistanceComparer;
    private Animator previewHandAnimator;
    private Transform previewHandTransform;
    private Grab currentGrab;
    private List<Grab> frozenGrabs;
    private const float springForce = 10000f;
    private const float breakForce = 4000f;
    private const float maxGrabDistance = 2.5f;
    private bool previewGrab;
    private List<Grab> removeIds;
    
    [SerializeField]
    private Collider[] ignoreColliders;
    private class Grab {
        public PhotonView photonView { get; private set; }
        public Rigidbody body { get; private set; }
        public Vector3 localColliderPosition { get; private set; }
        public Vector3 localHitNormal { get; private set; }
        public bool affectingRotation { get; private set; }
        public Kobold targetKobold { get; private set; }

        private ConfigurableJoint joint;
        private Collider collider;
        private Quaternion savedQuaternion;
        private Animator handDisplayAnimator;
        private Transform handTransform;
        private float distance;
        private Kobold owner;
        private Vector3 bodyAnchor;
        private Transform view;
        private bool frozen;
        private AudioPack unfreezePack;
        private Quaternion startRotation;
        private float creationTime;

        public Collider GetCollider() {
            return collider;
        }

        private ConfigurableJoint AddJoint(Vector3 worldPosition, Quaternion targetRotation, bool affRotation) {
            Quaternion save = body.rotation;
            if (!affRotation) {
                body.transform.rotation = Quaternion.identity;
            }

            startRotation = body.transform.rotation;
            ConfigurableJoint configurableJoint = body.gameObject.AddComponent<ConfigurableJoint>();
            configurableJoint.axis = Vector3.up;
            configurableJoint.secondaryAxis = Vector3.right;
            configurableJoint.connectedBody = null;
            configurableJoint.autoConfigureConnectedAnchor = false;
            if (owner.photonView.IsMine) {
                configurableJoint.breakForce = breakForce;
            } else {
                configurableJoint.breakForce = float.MaxValue;
            }
            JointDrive drive = configurableJoint.xDrive;
            drive.positionSpring = springForce;
            drive.positionDamper = 2f;
            configurableJoint.xDrive = drive;
            configurableJoint.yDrive = drive;
            configurableJoint.zDrive = drive;
            var linearLimit = configurableJoint.linearLimit;
            linearLimit.limit = 1f;
            linearLimit.bounciness = 0f;
            var spring = configurableJoint.linearLimitSpring;
            spring.spring = springForce * 0.1f;
            spring.damper = 2f;
            configurableJoint.linearLimitSpring = spring;
            configurableJoint.linearLimit = linearLimit;
            configurableJoint.rotationDriveMode = RotationDriveMode.Slerp;
            configurableJoint.massScale = 1f;
            configurableJoint.projectionMode = JointProjectionMode.PositionAndRotation;
            configurableJoint.projectionAngle = 10f;
            configurableJoint.projectionDistance = 0.5f;
            configurableJoint.connectedMassScale = 1f;
            configurableJoint.enablePreprocessing = false;
            configurableJoint.configuredInWorldSpace = true;
            configurableJoint.anchor = body.transform.InverseTransformPoint(collider.transform.TransformPoint(localColliderPosition));
            configurableJoint.configuredInWorldSpace = true;
            configurableJoint.connectedBody = null;
            configurableJoint.connectedAnchor = worldPosition;
            //var slerpDrive = configurableJoint.slerpDrive;
            //slerpDrive.positionSpring = springForce*2f;
            //slerpDrive.maximumForce = float.MaxValue;
            //slerpDrive.positionDamper = 2f;
            //configurableJoint.slerpDrive = slerpDrive;
            configurableJoint.xMotion = ConfigurableJointMotion.Limited;
            configurableJoint.yMotion = ConfigurableJointMotion.Limited;
            configurableJoint.zMotion = ConfigurableJointMotion.Limited;
            if (affRotation) {
                configurableJoint.SetTargetRotation(targetRotation, startRotation);
                configurableJoint.angularXMotion = ConfigurableJointMotion.Locked;
                configurableJoint.angularYMotion = ConfigurableJointMotion.Locked;
                configurableJoint.angularZMotion = ConfigurableJointMotion.Locked;
            } else {
                body.transform.rotation = save;
            }

            return configurableJoint;
        }
        public Grab(Kobold owner, GameObject handDisplayPrefab, Transform view, Collider collider,
            Vector3 localColliderPosition, Vector3 localHitNormal, AudioPack unfreezePack) {
            this.collider = collider;
            this.localColliderPosition = localColliderPosition;
            this.localHitNormal = localHitNormal;
            this.owner = owner;
            this.view = view;
            this.unfreezePack = unfreezePack;
            body = collider.GetComponentInParent<Rigidbody>();
            if (body == null ) {
                return;
            }
            savedQuaternion = body.rotation;
            Vector3 hitPosWorld = collider.transform.TransformPoint(localColliderPosition);
            bodyAnchor = body.transform.InverseTransformPoint(hitPosWorld);
            distance = Vector3.Distance(view.position, hitPosWorld);
            creationTime = Time.time;
            handDisplayAnimator = GameObject.Instantiate(handDisplayPrefab, owner.transform)
                .GetComponentInChildren<Animator>();
            handDisplayAnimator.gameObject.SetActive(true);
            handDisplayAnimator.SetBool(GrabbingHash, true);
            handTransform = handDisplayAnimator.GetBoneTransform(HumanBodyBones.RightHand);
            photonView = collider.GetComponentInParent<PhotonView>();
            frozen = false;
            targetKobold = collider.GetComponentInParent<Kobold>();
            if (targetKobold != null) {
                targetKobold.ragdoller.PushRagdoll();
                body.maxAngularVelocity = 10f;
            } else {
                body.maxAngularVelocity = 20f;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            }
            joint = AddJoint(hitPosWorld, Quaternion.identity, false);
            if (owner.photonView.IsMine) {
                photonView.RequestOwnership();
            }
        }
        
        public Grab(Kobold owner, GameObject handDisplayPrefab, Collider collider, Vector3 localColliderPosition,
            Vector3 localHitNormal, Vector3 worldAnchor, Quaternion rotation, bool affRotation, AudioPack unfreezePack) {
            this.collider = collider;
            this.localColliderPosition = localColliderPosition;
            this.localHitNormal = localHitNormal;
            savedQuaternion = rotation;
            body = collider.GetComponentInParent<Rigidbody>();
            photonView = collider.GetComponentInParent<PhotonView>();
            Vector3 hitPosWorld = collider.transform.TransformPoint(localColliderPosition);
            bodyAnchor = body.transform.InverseTransformPoint(hitPosWorld);
            handDisplayAnimator = GameObject.Instantiate(handDisplayPrefab, owner.transform)
                .GetComponentInChildren<Animator>();
            handDisplayAnimator.gameObject.SetActive(true);
            handDisplayAnimator.SetBool(GrabbingHash, true);
            handTransform = handDisplayAnimator.GetBoneTransform(HumanBodyBones.RightHand);
            creationTime = Time.time;
            this.owner = owner;
            this.unfreezePack = unfreezePack;
            frozen = true;
            joint = AddJoint(worldAnchor, rotation, affRotation);
            targetKobold = collider.GetComponentInParent<Kobold>();
            if (targetKobold != null) {
                targetKobold.ragdoller.PushRagdoll();
                body.maxAngularVelocity = 10f;
            } else {
                body.maxAngularVelocity = 20f;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            }

            if (owner.photonView.IsMine) {
                photonView.RequestOwnership();
            }
        }

        public void SetVisibility(float newVisibility) {
            if (newVisibility < 0.5f && handDisplayAnimator.gameObject.activeSelf) {
                handDisplayAnimator.gameObject.SetActive(false);
            } else if (newVisibility >= 0.5f && !handDisplayAnimator.gameObject.activeSelf) {
                handDisplayAnimator.gameObject.SetActive(true);
            }
            handDisplayAnimator.SetBool(GrabbingHash, true);
        }

        public float GetDistance() => distance;
        public Quaternion GetRotation() => savedQuaternion;

        public void Freeze() {
            if (frozen) {
                return;
            }
            frozen = true;
            if (joint != null) {
                Destroy(joint);
            }
            joint = AddJoint(collider.transform.TransformPoint(localColliderPosition), savedQuaternion, affectingRotation);
        }

        public Vector3 GetWorldPosition() {
            if (collider != null) {
                return collider.transform.TransformPoint(localColliderPosition);
            }
            if (handTransform != null) {
                return handTransform.transform.position;
            }
            return Vector3.zero;
        }

        public bool Valid() {
            bool valid = body != null && owner != null && photonView != null && joint != null;
            if (Time.time - creationTime > 2f && valid) {
                valid &= Equals(photonView.Controller, owner.photonView.Controller);
            }
            return valid;
        }
        public void LateUpdate() {
            Vector3 worldNormal = collider.transform.TransformDirection(localHitNormal);
            Vector3 worldPoint = collider.transform.TransformPoint(localColliderPosition);
            handTransform.rotation = Quaternion.LookRotation(-worldNormal, Vector3.up) * Quaternion.AngleAxis(90f, Vector3.up);
            handTransform.position = worldPoint + handTransform.rotation * (Vector3.down*0.1f);
        }

        public void SetRotation(Quaternion rot) {
            savedQuaternion = rot;
            if (!affectingRotation) {
                affectingRotation = true;
            }
        }

        public void SetDistance(float dist) {
            distance = dist;
        }

        public void Rotate(Vector2 delta) {
            if (!affectingRotation) {
                affectingRotation = true;
                savedQuaternion = body.transform.rotation;
                var slerpDrive = joint.slerpDrive;
                slerpDrive.positionSpring = springForce;
                slerpDrive.maximumForce = float.MaxValue;
                slerpDrive.positionDamper = 2f;
                joint.slerpDrive = slerpDrive;
            }
            savedQuaternion = Quaternion.AngleAxis(-delta.x, view.up)*savedQuaternion;
            savedQuaternion = Quaternion.AngleAxis(delta.y, view.right)*savedQuaternion;
        }

        public void AdjustDistance(float delta) {
            distance += delta;
            distance = Mathf.Max(distance, 0f);
        }

        public void FixedUpdate() {
            if (frozen) {
                return;
            }

            Vector3 holdPoint = view.position + view.forward * distance;

            if (joint != null) {
                joint.connectedAnchor = holdPoint;
                if (affectingRotation) {
                    joint.SetTargetRotation(savedQuaternion, startRotation);
                }
            }

            // Manual axis alignment, for pole jumps!
            if (!body.transform.IsChildOf(owner.body.transform) && !body.isKinematic) {
                body.velocity -= body.velocity * 0.5f;
                Vector3 axis = view.forward;
                Vector3 jointPos = body.transform.TransformPoint(bodyAnchor);
                Vector3 center = (view.position + jointPos) / 2f;
                Vector3 wantedPosition1 = center - axis * distance / 2f;
                //Vector3 wantedPosition2 = center + axis * distance / 2f;
                float ratio = Mathf.Clamp((body.mass / owner.body.mass), 0.75f, 1.25f);
                Vector3 force = (wantedPosition1 - view.position) * (springForce * 0.15f);
                owner.body.AddForce(force * ratio);
                //body.AddForce(-force * (1f / ratio));
            }
        }

        public void Release() {
            GameManager.instance.SpawnAudioClipInWorld(unfreezePack, GetWorldPosition());
            if (joint != null) {
                Destroy(joint);
            }
            Destroy(handDisplayAnimator.gameObject);
            if (body != null && targetKobold == null) {
                body.collisionDetectionMode = CollisionDetectionMode.Discrete;
                body.interpolation = RigidbodyInterpolation.None;
                body.maxAngularVelocity = Physics.defaultMaxAngularSpeed;
            }
            
            if (targetKobold != null) {
                targetKobold.ragdoller.PopRagdoll();
                if (targetKobold == (Kobold)PhotonNetwork.LocalPlayer.TagObject) {
                    foreach (Grab grab in owner.GetComponent<PrecisionGrabber>().frozenGrabs) {
                        if (grab.targetKobold == targetKobold) {
                            return;
                        }
                    }
                    // If we're no longer grabbed. Request ownership back.
                    targetKobold.photonView.RequestOwnership();
                }
            }
        }
    }

    private class RaycastHitDistanceComparer : IComparer {
        public int Compare(object x, object y) {
            RaycastHit a = (RaycastHit)x;
            RaycastHit b = (RaycastHit)y;
            return a.distance.CompareTo(b.distance);
        }
    }

    private void Awake() {
        raycastHitDistanceComparer = new RaycastHitDistanceComparer();
        previewHandAnimator = GameObject.Instantiate(handDisplayPrefab, transform)
            .GetComponentInChildren<Animator>();
        previewHandAnimator.SetBool(GrabbingHash, true);
        previewHandTransform = previewHandAnimator.GetBoneTransform(HumanBodyBones.RightHand);
        previewHandAnimator.gameObject.SetActive(false);
        kobold = GetComponent<Kobold>();
        frozenGrabs = new List<Grab>();
    }

    private void Start() {
        kobold.genesChanged += OnGenesChanged;
        OnGenesChanged(kobold.GetGenes());
        removeIds = new List<Grab>();
        handVisibilityEvent.AddListener(OnHandVisibilityChanged);
    }

    private void OnHandVisibilityChanged(float newVisibility) {
        foreach (var fgrab in frozenGrabs) {
            fgrab.SetVisibility(newVisibility);
        }
    }

    private void OnDestroy() {
        if (kobold != null) {
            kobold.genesChanged -= OnGenesChanged;
        }
        handVisibilityEvent.RemoveListener(OnHandVisibilityChanged);
        TryDrop();
        UnfreezeAll();
    }

    private void OnGenesChanged(KoboldGenes newGenes) {
        if (newGenes == null) {
            return;
        }

        Vector4 hbcs = new Vector4(newGenes.hue/255f, newGenes.brightness/255f, 0.5f, newGenes.saturation/255f);
        // Set color
        foreach (Renderer r in previewHandAnimator.GetComponentsInChildren<Renderer>()) {
            if (r == null) {
                continue;
            }
            foreach (Material m in r.materials) {
                m.SetVector(BrightnessContrastSaturation, hbcs);
            }
        }
    }

    private bool TryRaycastGrab(float maxDistance, out RaycastHit? previewHit) {
        int numHits = Physics.RaycastNonAlloc(view.position, view.forward, hits, maxDistance, GameManager.instance.precisionGrabMask, QueryTriggerInteraction.Ignore);
        if (numHits == 0) {
            previewHit = null;
            return false;
        }
        Array.Sort(hits, 0, numHits, raycastHitDistanceComparer);
        for (int i = 0; i < numHits; i++) {
            RaycastHit hit = hits[i];
            if (ignoreColliders.Contains(hit.collider)) {
                continue;
            }
            if (hit.distance > maxDistance) {
                continue;
            }

            previewHit = hit;
            return true;
        }
        previewHit = null;
        return false;
    }

    public void SetPreviewState(bool previewEnabled) {
        previewGrab = previewEnabled;
    }

    private void DoPreview() {
        if (previewGrab && currentGrab == null && TryRaycastGrab(maxGrabDistance, out RaycastHit? previewHit)) {
            RaycastHit hit = previewHit.Value;
            previewHandTransform.rotation = Quaternion.LookRotation(-hit.normal, Vector3.up) * Quaternion.AngleAxis(90f, new Vector3(0.0f, 1.0f, 0.0f));
            previewHandTransform.position = hit.point + previewHandTransform.rotation*Vector3.down*0.1f;
            if (!previewHandAnimator.gameObject.activeInHierarchy) {
                previewHandAnimator.gameObject.SetActive(true);
            }
        } else {
            if (previewHandAnimator.gameObject.activeInHierarchy) {
                previewHandAnimator.gameObject.SetActive(false);
            }
        }
    }

    public bool HasGrab() {
        return currentGrab != null;
    }

    public bool TryRotate(Vector2 delta) {
        if (currentGrab == null) {
            return false;
        }
        currentGrab.Rotate(delta);
        return true;
    }
    
    public bool TryAdjustDistance(float delta) {
        if (currentGrab == null) {
            return false;
        }
        currentGrab.AdjustDistance(delta);
        return true;
    }

    [PunRPC]
    private void GrabRPC(int viewID, int colliderNum, Vector3 localHit, Vector3 localHitNormal) {
        PhotonView otherPhotonView = PhotonNetwork.GetPhotonView(viewID);
        if (otherPhotonView == null) {
            return;
        }
        Collider[] colliders = otherPhotonView.GetComponentsInChildren<Collider>();
        currentGrab = new Grab(kobold, previewHandAnimator.gameObject, view, colliders[colliderNum], localHit, localHitNormal, unfreezeSound);
        if (!currentGrab.Valid()) {
            currentGrab = null;
            return;
        }
        currentGrab.SetVisibility(handVisibilityEvent.GetLastInvokeValue());
    }

    public void TryGrab() {
        if (currentGrab != null || !photonView.IsMine) {
            return;
        }
        if (!TryRaycastGrab(maxGrabDistance, out RaycastHit? hitTest)) {
            return;
        }

        RaycastHit hit = hitTest.Value;
        Vector3 localHit = hit.collider.transform.InverseTransformPoint(hit.point);
        Vector3 localHitNormal = hit.collider.transform.InverseTransformDirection(hit.normal);

        PhotonView otherView = hit.collider.GetComponentInParent<PhotonView>();
        Collider[] colliders = otherView.GetComponentsInChildren<Collider>();
        for (int i = 0; i < colliders.Length; i++) {
            if (colliders[i] == hit.collider) {
                photonView.RPC(nameof(GrabRPC), RpcTarget.All,  otherView.ViewID, i, localHit, localHitNormal);
                break;
            }
        }
    }

    [PunRPC]
    private void FreezeRPC(int grabViewID, int colliderNum, Vector3 localColliderPosition, Vector3 localHitNormal, Vector3 worldAnchor, Quaternion rotation, bool affRotation) {
        PhotonView grabView = PhotonNetwork.GetPhotonView(grabViewID);
        Collider[] colliders = grabView.GetComponentsInChildren<Collider>();
        frozenGrabs.Add(new Grab(kobold, previewHandAnimator.gameObject, colliders[colliderNum], localColliderPosition, localHitNormal,
            worldAnchor, rotation, affRotation, unfreezeSound));
        frozenGrabs[^1].SetVisibility(handVisibilityEvent.GetLastInvokeValue());
        Destroy(GameObject.Instantiate(freezeVFXPrefab, worldAnchor, Quaternion.identity), 5f);
    }

    public bool TryFreeze() {
        if (currentGrab == null || !photonView.IsMine) {
            return false;
        }
        // Tell everyone we've made a frozen joint here.
        Collider[] colliders = currentGrab.photonView.GetComponentsInChildren<Collider>();
        for (int i = 0; i < colliders.Length; i++) {
            if (currentGrab.GetCollider() == colliders[i]) {
                photonView.RPC(nameof(FreezeRPC), RpcTarget.All, currentGrab.photonView.ViewID,
                    i, currentGrab.localColliderPosition, currentGrab.localHitNormal, currentGrab.GetWorldPosition(),
                    currentGrab.body.rotation, currentGrab.affectingRotation);
                TryDrop();
                return true;
            }
        }

        return false; // Should never happen
    }

    [PunRPC]
    private void UnfreezeRPC(int viewID, int rigidbodyID) {
        PhotonView checkView = PhotonNetwork.GetPhotonView(viewID);
        Rigidbody[] bodies = checkView.GetComponentsInChildren<Rigidbody>();
        for(int i=0;i<frozenGrabs.Count;i++) {
            Grab frozenGrab = frozenGrabs[i];
            if (frozenGrab.body == bodies[rigidbodyID]) {
                frozenGrab.Release();
                frozenGrabs.RemoveAt(i--);
            }
        }
    }

    [PunRPC]
    private void UnfreezeAllRPC() {
        foreach (var frozenGrab in frozenGrabs) {
            frozenGrab.Release();
        }
        frozenGrabs.Clear();
    }

    public bool TryUnfreeze() {
        if (!photonView.IsMine) {
            return false;
        }

        if (!TryRaycastGrab(100f, out RaycastHit? testHit)) {
            return false;
        }

        bool foundGrabs = false;
        RaycastHit hit = testHit.Value;
        for(int i=0;i<frozenGrabs.Count;i++) {
            Grab frozenGrab = frozenGrabs[i];
            if (frozenGrab.body == hit.rigidbody) {
                //frozenGrab.Release();
                //frozenGrabs.RemoveAt(i--);
                foundGrabs = true;
            }
        }
        
        PhotonView otherView = hit.collider.GetComponentInParent<PhotonView>();
        if (foundGrabs) {
            Rigidbody[] bodies = otherView.GetComponentsInChildren<Rigidbody>();
            for (int i = 0; i < bodies.Length; i++) {
                if (hit.rigidbody == bodies[i]) {
                    photonView.RPC(nameof(UnfreezeRPC), RpcTarget.All, otherView.ViewID, i);
                    break;
                }
            }
        }

        return foundGrabs;
    }

    public void UnfreezeAll() {
        if (frozenGrabs.Count > 0 && photonView.IsMine) {
            UnfreezeAllRPC();
            photonView.RPC(nameof(UnfreezeAllRPC), RpcTarget.Others);
        }
    }

    [PunRPC]
    private void DropRPC() {
        currentGrab?.Release();
        currentGrab = null;
    }
    
    public void TryDrop() {
        if (currentGrab != null && photonView.IsMine) {
            photonView.RPC(nameof(DropRPC), RpcTarget.All);
            return;
        }
        
        currentGrab?.Release();
        currentGrab = null;
    }

    private void LateUpdate() {
        DoPreview();
        Validate();
        currentGrab?.LateUpdate();
        foreach (var f in frozenGrabs) {
            f.LateUpdate();
        }
    }
    private IEnumerator GiveBackKoboldsWhenPossible(Kobold targetKobold, float delay) {
        yield return new WaitForSeconds(delay);
        bool isPlayerKobold = false;
        foreach (var playerCheck in PhotonNetwork.PlayerList) {
            if (ReferenceEquals((Kobold)playerCheck.TagObject, targetKobold) && playerCheck != PhotonNetwork.LocalPlayer) {
                isPlayerKobold = true;
                break;
            }
        }
        while (targetKobold != null && targetKobold.photonView.IsMine && isPlayerKobold) {
            foreach (var playerCheck in PhotonNetwork.PlayerList) {
                if (ReferenceEquals((Kobold)playerCheck.TagObject, targetKobold)) {
                    targetKobold.photonView.TransferOwnership(playerCheck);
                }
            }
            yield return new WaitForSeconds(1f);
        }
    }

    private void Validate() {
        if (currentGrab != null && !currentGrab.Valid()) {
            TryDrop();
        }

        removeIds.Clear();
        foreach (var f in frozenGrabs) {
            if (!f.Valid()) {
                removeIds.Add(f);
            }
        }

        foreach (Grab fgrab in removeIds) {
            if (photonView.IsMine && fgrab.photonView != null) {
                Rigidbody[] bodies = fgrab.photonView.GetComponentsInChildren<Rigidbody>();
                for (int i = 0; i < bodies.Length; i++) {
                    if (bodies[i] == fgrab.body) {
                        photonView.RPC(nameof(UnfreezeRPC), RpcTarget.All, fgrab.photonView.ViewID, i);
                        break;
                    }
                }
            } else {
                fgrab.Release();
                frozenGrabs.Remove(fgrab);
            }
        }

        removeIds.Clear();
        if (!freezeUI.activeSelf && frozenGrabs.Count > 0 || freezeUI.activeSelf && frozenGrabs.Count == 0) {
            freezeUI.SetActive(frozenGrabs.Count != 0);
        }

        if (currentGrab != null && !activeUI[0].activeSelf || currentGrab == null && activeUI[0].activeSelf) {
            foreach (var ui in activeUI) {
                ui.SetActive(currentGrab != null);
            }
        }
    }


    private void FixedUpdate() {
        Validate();
        currentGrab?.FixedUpdate();
        foreach (var grab in frozenGrabs) {
            grab.FixedUpdate();
        }
    }

    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info) {
        if (stream.IsWriting) {
            if (currentGrab != null) {
                stream.SendNext(currentGrab.GetRotation());
                stream.SendNext(currentGrab.GetDistance());
            } else {
                stream.SendNext(Quaternion.identity);
                stream.SendNext(2f);
            }
        } else {
            Quaternion rot = (Quaternion)stream.ReceiveNext();
            float dist  = (float)stream.ReceiveNext();
            if (currentGrab != null) {
                currentGrab.SetRotation(rot);
                currentGrab.SetDistance(dist);
            }
        }
    }

    public void Save(BinaryWriter writer) {
    }

    public void Load(BinaryReader reader) {
    }
}
