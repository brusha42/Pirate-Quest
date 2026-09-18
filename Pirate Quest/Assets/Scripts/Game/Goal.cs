using System;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider2D))]
public class Goal : MonoBehaviour
{
    public event Action Reached;

    private bool wasReached;

    private void Awake()
    {
        GetComponent<Collider2D>().isTrigger = true;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        PlayerLife life = other.GetComponent<PlayerLife>();
        if (wasReached || life == null || life.IsRespawning)
        {
            return;
        }

        wasReached = true;
        Reached?.Invoke();
    }
}
