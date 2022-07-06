using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using System;
using Lean.Touch;

[RequireComponent(typeof(LeanSelectable))]
public class Interaction : MonoBehaviour
{
    [Tooltip("This event is called when you click on this object if it has been focussed by the camera, Unless it has no POI component then it is always called when you click on the object.")]
    public UnityEvent InteractEvent;

    private LeanSelectable leanSelectable;

    public void Interact(LeanFinger leanFinger)
    {
        if (TryGetComponent(out PointOfInterest pointOfInterest))
        {
            if (pointOfInterest.IsFocused)
            {
                InteractEvent.Invoke();
                //Debug.Log("Invoked interact event (Focussed on POI");
            }
        }
        else
        {
            InteractEvent.Invoke();
            //Debug.Log("Invoked interact event (No POI)");
        }
    }
    private void OnEnable()
    {
        leanSelectable.OnSelectUp.AddListener(Interact);
    }

    private void OnDisable()
    {
        leanSelectable.OnSelectUp.RemoveListener(Interact);
    }

    private void Awake()
    {
        leanSelectable = GetComponent<LeanSelectable>();
    }

    //temp example
    public void SimpleFall(Transform fallingObject)
    {
        LeanTween.rotateX(fallingObject.gameObject, 85, 0.5f).setEaseOutBounce();
    }
}
