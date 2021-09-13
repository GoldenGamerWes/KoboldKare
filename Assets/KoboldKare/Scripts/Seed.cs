﻿using KoboldKare;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Events;
using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using Photon;
using ExitGames.Client.Photon;

public class Seed : MonoBehaviour, IValuedGood {
    //public List<GameObject> _plantPrefabs;
    public PhotonGameObjectReference plantPrefab;
    public int type = 0;
    public float _spacing = 1f;
    public UnityEvent OnFailPlant;
    private Collider[] hitColliders = new Collider[4];
    public bool ShouldSave() {
        return true;
    }
    public bool CanPlant() {
        //fix this function or so help you...
        int hitCount = Physics.OverlapSphereNonAlloc(transform.position, _spacing, hitColliders, GameManager.instance.plantHitMask, QueryTriggerInteraction.Ignore);
        for(int i=0;i<hitCount;i++) {
            if (!hitColliders[i].CompareTag("PlantableTerrain") && !hitColliders[i].CompareTag("NoBlockPlant")) {
                return false;
            }
        }
        RaycastHit hit;
        if (Physics.Raycast(transform.position + Vector3.up, Vector3.down, out hit, 2f, GameManager.instance.plantHitMask, QueryTriggerInteraction.Collide)) {
            if (hit.collider.CompareTag("PlantableTerrain")) {
                if (Physics.Raycast(transform.position + Vector3.up, Vector3.down, out hit, 2f, GameManager.instance.plantHitMask, QueryTriggerInteraction.Ignore)) {
                    return true;
                }
            }
        }
        return false;
    }
    public void Plant() {
        if (!GetComponentInParent<PhotonView>().IsMine) {
            return;
        }
        int hitCount = Physics.OverlapSphereNonAlloc(transform.position, _spacing, hitColliders, GameManager.instance.plantHitMask, QueryTriggerInteraction.Ignore);
        for(int i=0;i<hitCount;i++) {
            if (!hitColliders[i].CompareTag("PlantableTerrain") && !hitColliders[i].CompareTag("NoBlockPlant")) {
                return;
            }
        }
        RaycastHit hit;
        if (Physics.Raycast(transform.position + Vector3.up, Vector3.down, out hit, 2f, GameManager.instance.plantHitMask, QueryTriggerInteraction.Collide)) {
            if (hit.collider.CompareTag("PlantableTerrain")) {
                if (Physics.Raycast(transform.position + Vector3.up, Vector3.down, out hit, 2f, GameManager.instance.plantHitMask, QueryTriggerInteraction.Ignore)) {
                    GameObject g = SaveManager.Instantiate(plantPrefab.photonName, hit.point, Quaternion.LookRotation(Vector3.forward, hit.normal), 0, null);
                    SaveManager.Destroy(gameObject);
                }
            }
        }
    }
    public float GetWorth() {
        return 5f;
    }
    public void OnValidate() {
        plantPrefab.OnValidate();
    }
}
