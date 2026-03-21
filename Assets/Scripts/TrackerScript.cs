using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public class TrackerScript : MonoBehaviour
{
    //Utility list to assign models to image names via the Unity Inspector.
    //ImagePrefabPair is a custom struct in the same file (see the bottom).
    [SerializeField] private List<ImagePrefabPair> pairs = new();

    [SerializeField] private float AnchorTimerFixed = 5f;

    private ARTrackedImageManager imageManager;
    private ARAnchorManager anchorManager;

    //Map image names to models you want to spawn.
    private readonly Dictionary<string, GameObject> modelsMap = new();

    //Map trackable IDs to their corresponding models.
    //Only previously spawned models' images can have an ID (safer to use).
    //AR Core assigns them on first image detection.
    private readonly Dictionary<TrackableId, GameObject> modelsSpawned = new();

    private readonly Dictionary<TrackableId, ARAnchor> anchors = new();
    private readonly HashSet<TrackableId> pendingAnchors = new();
    private readonly Dictionary<TrackableId, float> anchorTimers = new();

    private void OnEnable()
    {
        imageManager = GetComponent<ARTrackedImageManager>();
        imageManager.trackablesChanged.AddListener(OnTrackedImagesChanged);

        anchorManager = GetComponent<ARAnchorManager>();
    }

    private void OnDisable()
    {
        imageManager.trackablesChanged.RemoveListener(OnTrackedImagesChanged);
    }

    private void Awake()
    {
        foreach(var pair in pairs)
        {
            modelsMap[pair.name] = pair.prefab;
        }
    }

    private void OnTrackedImagesChanged(ARTrackablesChangedEventArgs<ARTrackedImage> args)
    {
        //Image has been detected and is now tracked.
        foreach(var image in args.added)
        {
            if(modelsSpawned.TryGetValue(image.trackableId, out var modelFromCache))
            {
                modelFromCache.SetActive(true);
            }
            else if(modelsMap.TryGetValue(image.referenceImage.name, out var model))
            {
                var spawnedModel = Instantiate(model, image.transform.position, image.transform.rotation);
                modelsSpawned[image.trackableId] = spawnedModel;
            }
        }

        //Image is still being tracked.
        //Can set TrackingState to "Tracking", "Limited", "None".
        //Even when the image is not detected by the camera, it still lives within as a cached object
        //  and its state is updated.
        foreach(var image in args.updated)
        {
            var trackableId = image.trackableId;

            if (!modelsSpawned.TryGetValue(trackableId, out var model)) continue;

            if(image.trackingState == TrackingState.Tracking)
            {
                model.SetActive(true);
                model.transform.SetPositionAndRotation(image.transform.position, image.transform.rotation);

                anchorTimers.Remove(trackableId);

                if (anchors.TryGetValue(trackableId, out var existingAnchor))
                {
                    CleanupAnchor(existingAnchor, trackableId, model);
                }

            }
            else
            {
                if (!anchors.ContainsKey(trackableId))
                {
                    CreateAnchor(trackableId, model);
                }

                if (!anchorTimers.ContainsKey(trackableId))
                {
                    anchorTimers[trackableId] = 0f;
                }

                anchorTimers[trackableId] += Time.deltaTime;

                if (anchorTimers[trackableId] > AnchorTimerFixed)
                {
                    model.SetActive(false);

                    if (anchors.TryGetValue(trackableId, out var existingAnchor))
                    {
                        CleanupAnchor(existingAnchor, trackableId, model);
                    }
                }
            }
        }

        //Fallback, whenever ARTrackingManager is disabled, AR Session resets or some internal cleanup is done.
        foreach(var (trackableId, _) in args.removed)
        {
            if(modelsSpawned.TryGetValue(trackableId, out var model))
            {
                model.SetActive(false);
            }
        }
    }

    private async void CreateAnchor(TrackableId trackableId, GameObject spawnedModel)
    {
        if(!pendingAnchors.Add(trackableId)) return;

        var result = await anchorManager.TryAddAnchorAsync(new Pose(spawnedModel.transform.position, spawnedModel.transform.rotation));
        
        pendingAnchors.Remove(trackableId);

        if (result.status.IsSuccess())
        {
            if (!anchorTimers.ContainsKey(trackableId))
            {
                Destroy(result.value.gameObject);
                return;
            }

            anchors[trackableId] = result.value;
            spawnedModel.transform.SetParent(result.value.transform);
        }
    }

    private void CleanupAnchor(ARAnchor existingAnchor, TrackableId trackableId, GameObject spawnedModel)
    {
        spawnedModel.transform.SetParent(null);
        Destroy(existingAnchor.gameObject);
        anchors.Remove(trackableId);
        anchorTimers.Remove(trackableId);
    }
}

[Serializable]
public struct ImagePrefabPair
{
    public string name;
    public GameObject prefab;
}