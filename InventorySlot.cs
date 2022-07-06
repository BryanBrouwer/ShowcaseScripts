using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class InventorySlot : MonoBehaviour
{
    public bool occupied = false;
    public ObjectId storedObjectId;
    private Vector3 defaultPosition;

    private void Awake()
    {
        defaultPosition = transform.localPosition;
    }

    public Item Item
    {
        get
        {
            Debug.Assert(storedObjectId != null, "StoredItemId has not been assigned yet!");

            item = storedObjectId.GetComponent<Item>();
            return item;
        }
    }

    public void RestoreDefaultPosition()
    {
        transform.localPosition = defaultPosition;
    }
    private Item item;
}
