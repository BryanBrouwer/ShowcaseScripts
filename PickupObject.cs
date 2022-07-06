using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Visibility))]
[RequireComponent(typeof(ObjectId))]
[RequireComponent(typeof(Collider))]
[RequireComponent(typeof(PointOfInterest))]
public class PickupObject : Item
{
    [SerializeField] private bool locked = false;
    //[SerializeField] private TimelineType timelineDependency = TimelineType.Present;
    private TimelineType timelineDependency = TimelineType.Present;

    private Visibility visibility;
    private ObjectId objectId;

    public override bool CanPickup()
    {
        return (!locked && GetComponent<PointOfInterest>().IsFocused);
    }

    public override void Pickup() 
    {
        if(!CanPickup()) { return; }

        pickedUp = true;

        Inventory.Instance.AddToInventory(objectId);

        SetVisible(false);
        SetCollideable(false);

        GameState.Instance.Save();
    }

    public override void SetLocked(bool lockstate)
    {
        locked = lockstate;
    }

    public override void SetVisible(bool newVisibility)
    {
        visibility.SetVisible(newVisibility);
    }

    public override void SetCollideable(bool collideable)
    {
        visibility.AfterSetCollider(collideable);
    }

    private void Awake()
    {
        objectId = GetComponent<ObjectId>();
        visibility = GetComponent<Visibility>();

        if (objectId == null)
        {
            Debug.Assert(false, "There is no objectId!", gameObject);
        }
    }

    public override void Init(bool newPickedUp, bool newPlaced, Vector3 placeLocation, Vector3 placeRotation)
    {
        pickedUp = newPickedUp;
        placed = newPlaced;

        if(placed)
        {
            //TODO set pos & rot
            transform.position = placeLocation;
            transform.rotation = Quaternion.Euler(placeRotation);
        }

        UpdateInterable();
    }

    public void UpdateInterable()
    {
        bool correctTimeline = timelineDependency == TimelineHelper.Instance.CurrentTimeline;

        if(pickedUp)
        {
            SetVisible(false);
            SetCollideable(false);
        } 
        else if(placed)
        {
            if(!correctTimeline)
            {
                SetVisible(true);
                SetCollideable(false);
            } 
            else
            {
                SetVisible(false);
                SetCollideable(false);
            }
        } 
        else if(correctTimeline)
        {
            SetVisible(true);
            SetCollideable(true);
        } 
        else
        {
            SetVisible(false);
            SetCollideable(false);
        }
    }

    public override void SetPlaced(Objective objective)
    {
        pickedUp = false;
        placed = true;
    }

    private void OnSwitchedTimeline(TimelineType newTimeline)
    {
        UpdateInterable();
    }

    private void OnEnable()
    {
        TimelineHelper.SwitchedTimelineEvent += OnSwitchedTimeline;
    }

    private void OnDisable()
    {
        TimelineHelper.SwitchedTimelineEvent -= OnSwitchedTimeline;
    }

    private void OnValidate()
    {
        if (ItemSprite == null)
        {
            Debug.LogWarning("Object has no sprite assigned.", gameObject);
        }
    }
}
