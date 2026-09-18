using UnityEngine;

public class PirateEquipmentVisual : MonoBehaviour
{
    private PlayerAbilities abilities;
    private PlayerMovement movement;
    private PlayerLife life;
    private SpriteRenderer blade;
    private float swingStarted;
    private void Awake()
    {
        abilities = GetComponent<PlayerAbilities>();
        movement = GetComponent<PlayerMovement>();
        life = GetComponent<PlayerLife>();
        var art = new GameObject("Equipped saber");
        art.transform.SetParent(transform, false);
        blade = art.AddComponent<SpriteRenderer>();
        blade.sortingOrder = 38;
        blade.enabled = false;
        abilities.SaberSwung += OnSwing;
    }
    private void OnSwing() { swingStarted = Time.time; }
    private void LateUpdate()
    {
        if (abilities == null || blade == null) return;
        bool visible = abilities.SaberLevel > 0 && (abilities.IsAttacking || abilities.IsSpikeSliding) && (life == null || !life.IsRespawning);
        blade.enabled = visible;
        if (!visible) return;
        Sprite sprite = PirateWorldArt.GetSprite(abilities.SaberLevel >= 2 ? PirateArtKind.SaberUpgrade : PirateArtKind.Saber);
        if (sprite == null) return;
        blade.sprite = sprite;
        float scale = 1.25f / Mathf.Max(sprite.bounds.size.x, sprite.bounds.size.y);
        blade.transform.localScale = Vector3.one * scale;
        if (abilities.IsSpikeSliding)
        {
            blade.transform.localPosition = new Vector3(0.15f, -0.5f, 0f);
            blade.transform.localRotation = Quaternion.Euler(0f, 0f, -45f);
        }
        else
        {
            float phase = Mathf.Clamp01((Time.time - swingStarted) / 0.18f);
            blade.transform.localPosition = new Vector3(0.6f + Mathf.Sin(phase * Mathf.PI) * 0.2f, 0.15f, 0f);
            blade.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(90f, -95f, phase));
        }
    }
    private void OnDestroy() { if (abilities != null) abilities.SaberSwung -= OnSwing; }
}
