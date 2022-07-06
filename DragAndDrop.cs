using Lean.Touch;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;


public class DragAndDrop : MonoBehaviour
{
    private LayerMask ignoreLayer;
    private Lean.Touch.LeanFinger currentFinger;

    private void Awake()
    {
        ignoreLayer = LayerMask.GetMask("OuterLevel");
    }

    public void SetFinger(Lean.Touch.LeanFinger finger)
    {
        currentFinger = finger;
    }

    public void RaycastDrop()
    {     
        if(currentFinger != null)
        {
            if (gameObject.GetComponent<InventorySlot>().occupied)
            {
                #region UICast
                foreach (InventorySlot slot in Inventory.Instance.UiSlots)
                {
                    if (slot != gameObject.GetComponent<InventorySlot>())
                    {
                        RectTransform slotTransform = slot.transform as RectTransform;
                        if (RectTransformUtility.RectangleContainsScreenPoint(slotTransform, Input.mousePosition))
                        {
                            if (slot.occupied)
                            {
                                ItemCombining.Instance.CombineItems(slot.storedObjectId, slot);
                            }
                            return;
                        }
                    }
                }
                #endregion
                #region physicsCast
                RaycastHit hit;

                Ray ray = Camera.main.ScreenPointToRay(currentFinger.ScreenPosition);
                // Does the ray intersect any objects excluding the player layer
                if (Physics.Raycast(ray.origin, ray.direction, out hit, Mathf.Infinity, ~ignoreLayer))
                {
                    if (hit.collider.gameObject.TryGetComponent(out PlaceLocationObject objective) &&
                        Inventory.Instance.HasItemSelected)
                    {
                        if (objective.gameObject.TryGetComponent(out PointOfInterest poi))
                        {
                            if (poi.IsFocused)
                            {
                                objective.TryInteract(Inventory.Instance.SelectedItem);
                            }
                        }
                        else
                        {
                            objective.TryInteract(Inventory.Instance.SelectedItem);
                        }
                    }

                    Debug.DrawRay(ray.origin, ray.direction * hit.distance, Color.yellow);
                }
                else
                {
                    Debug.DrawRay(ray.origin, ray.direction * 1000, Color.black);
                }
                #endregion
            }
        }
    }
}