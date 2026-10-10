using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

public static class UIBehaviourExtensions
{
    public static void AddCustomEventListener(this UIBehaviour control, EventTriggerType type, UnityAction<BaseEventData> callback)
    {
        EventTrigger eventTrigger = control.gameObject.GetComponent<EventTrigger>();
        if (eventTrigger == null)
            eventTrigger = control.gameObject.AddComponent<EventTrigger>();

        EventTrigger.Entry matchedEntry = eventTrigger.triggers.Find((e) => e.eventID == type);

        if (matchedEntry == null)
        {
            matchedEntry = new();
            matchedEntry.eventID = type;
            eventTrigger.triggers.Add(matchedEntry);
        }
        matchedEntry.callback.AddListener(callback);

    }
    public static void RemoveCustomEventListener(this UIBehaviour control, EventTriggerType type, UnityAction<BaseEventData> callback)
    {
        EventTrigger eventTrigger = control.gameObject.GetComponent<EventTrigger>();
        if (eventTrigger == null)
            return;
        foreach (var existingEntry in eventTrigger.triggers)
        {
            if (existingEntry.eventID == type)
                existingEntry.callback.RemoveListener(callback);
        }
    }
}
