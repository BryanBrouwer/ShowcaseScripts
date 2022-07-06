using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Events;
using UnityEngine.UI;

public class Inventory : MonoBehaviour
{
    #region Singleton
    public static Inventory Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindObjectOfType<Inventory>();
            }
            return instance;
        }
    }

    private static Inventory instance;
    #endregion

    public Sprite DefaultInventorySprite;
    public UnityEvent OnDragStart;
    public UnityEvent OnDragEnd;

    public ObjectId SelectedItemId => selectedItemId;
    public Item SelectedItem => selectedSlot.Item;
    public bool HasItemSelected => selectedItemId != null;
    public bool IsDragging => isDragging;

    public List<InventorySlot> UiSlots;

    private const string EMPTY_INVENTORY_ID = "Empty";
    private InventorySlot availableSlot;
    private ObjectId selectedItemId;
    private InventorySlot selectedSlot;
    private bool isDragging = false;

    public bool AddToInventory(ObjectId itemId)
    {
        if (!CheckAvailableSlot()) { return false; }

        availableSlot.occupied = true;
        availableSlot.storedObjectId = itemId;
        availableSlot.gameObject.GetComponent<Image>().sprite = itemId.GetComponent<Item>().ItemSprite;
        return true;
    }

    public void PlaceOnTarget(PlaceLocationObject placeLocation)
    {
        if (placeLocation.Interact(SelectedItem))
        {
            selectedSlot.occupied = false;
            selectedSlot.storedObjectId = null;
            selectedSlot.gameObject.GetComponent<Image>().sprite = DefaultInventorySprite;
        }
    }

    public void EmptySelectedSlot()
    {
        selectedSlot.occupied = false;
        selectedSlot.storedObjectId = null;
        selectedSlot.gameObject.GetComponent<Image>().sprite = DefaultInventorySprite;
    }

    public void OverwriteSelectedSlot(ObjectId itemId)
    {
        EmptySelectedSlot();

        selectedSlot.occupied = true;
        selectedSlot.storedObjectId = itemId;
        selectedSlot.gameObject.GetComponent<Image>().sprite = itemId.GetComponent<Item>().ItemSprite;
    }

    public void OverwriteSlot(ObjectId itemId, InventorySlot slot)
    {
        EmptySelectedSlot();

        slot.occupied = true;
        slot.storedObjectId = itemId;
        slot.gameObject.GetComponent<Image>().sprite = itemId.GetComponent<Item>().ItemSprite;
    }

    private bool CheckAvailableSlot()
    {
        foreach (InventorySlot slot in UiSlots)
        {
            if (!slot.occupied)
            {
                availableSlot = slot;
                return true;
            }
        }
        return false;
    }

    public void SelectSlot(InventorySlot slot)
    {
        if (slot.GetComponent<InventorySlot>().occupied)
        {
            selectedSlot = slot;
            selectedItemId = slot.storedObjectId;
        }
    }

    public string[] GetInventoryIds()
    {
        string[] inventoryIds = new string[UiSlots.Count];

        for (int i = 0; i < UiSlots.Count; i++)
        {
            if (UiSlots[i].storedObjectId != null)
            {
                inventoryIds[i] = UiSlots[i].storedObjectId.Id;
            }
            else
            {
                inventoryIds[i] = EMPTY_INVENTORY_ID;
            }

        }

        return inventoryIds;
    }

    public void Init(string[] inventoryIds)
    {
        Debug.Assert(inventoryIds.Length == UiSlots.Count, "Should be the same count!");

        for (int i = 0; i < inventoryIds.Length; i++)
        {
            if (inventoryIds[i] == EMPTY_INVENTORY_ID) { continue; }

            ObjectId objectId = ObjectId.Find(inventoryIds[i]).GetComponent<ObjectId>();

            UiSlots[i].occupied = true;
            UiSlots[i].storedObjectId = objectId;
            UiSlots[i].gameObject.GetComponent<Image>().sprite = objectId.GetComponent<Item>().ItemSprite;
        }
    }

    public void DraggingStart()
    {
        isDragging = true;
        OnDragStart.Invoke();
    }

    public void DraggingEnd()
    {
        isDragging = false;
        OnDragEnd.Invoke();
    }

    public void ClearInventory()
    {
        foreach (var uiSlot in UiSlots)
        {
            uiSlot.occupied = false;
            uiSlot.storedObjectId = null;
            uiSlot.gameObject.GetComponent<Image>().sprite = DefaultInventorySprite;
        }
    }
}
