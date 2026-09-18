using System;
using UnityEngine;

[DisallowMultipleComponent]
public class SaberCuttable : MonoBehaviour
{
    public bool IsCut { get; private set; }
    public event Action CutPerformed;
    private GameObject blockingObject;
    public void Configure(GameObject blockedObject = null) => blockingObject = blockedObject;
    public void Cut()
    {
        if (IsCut) return;
        IsCut = true;
        foreach (Collider2D hitbox in GetComponentsInChildren<Collider2D>()) hitbox.enabled = false;
        if (blockingObject != null && blockingObject != gameObject) blockingObject.SetActive(false);
        CutPerformed?.Invoke();
        foreach (SpriteRenderer sprite in GetComponentsInChildren<SpriteRenderer>()) sprite.enabled = false;
    }
}
