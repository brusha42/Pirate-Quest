using UnityEngine;

[DisallowMultipleComponent]
public class DarkZone : MonoBehaviour
{
    private SpriteRenderer[] cells;
    private Vector2[] positions;
    private PlayerAbilities player;
    private Texture2D texture;
    private Sprite sprite;
    private Material smoothMask;
    private Mesh maskMesh;
    public Rect WorldBounds { get; private set; }
    public const float MaximumOpacity = 0.985f;
    public int RevealedCellCount { get; private set; }
    public void Initialize(Vector2 size)
    {
        player = FindFirstObjectByType<PlayerAbilities>();
        WorldBounds = new Rect((Vector2)transform.position - size * .5f, size);
        int columns = Mathf.Max(1, Mathf.CeilToInt(size.x));
        int rows = Mathf.Max(1, Mathf.CeilToInt(size.y));
        cells = new SpriteRenderer[columns * rows];
        positions = new Vector2[cells.Length];
        Shader maskShader = Resources.Load<Shader>("PirateDarkness");
        if (maskShader != null && maskShader.isSupported)
        {
            smoothMask = new Material(maskShader) { name = "Parrot reconnaissance darkness" };
            smoothMask.SetColor("_Tint", new Color(.008f, .016f, .027f, MaximumOpacity));
            var veil = new GameObject("Smooth darkness veil");
            veil.transform.SetParent(transform, false);
            maskMesh = new Mesh { name = "Dark gallery bounds" };
            maskMesh.vertices = new[] {
                new Vector3(-size.x*.5f,-size.y*.5f,0f),new Vector3(size.x*.5f,-size.y*.5f,0f),
                new Vector3(size.x*.5f,size.y*.5f,0f),new Vector3(-size.x*.5f,size.y*.5f,0f) };
            maskMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            maskMesh.RecalculateBounds();
            veil.AddComponent<MeshFilter>().sharedMesh = maskMesh;
            MeshRenderer renderer = veil.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = smoothMask;
            renderer.sortingOrder = 80;
            for (int row = 0; row < rows; row++)
            for (int col = 0; col < columns; col++)
                positions[row * columns + col] = (Vector2)transform.position +
                    new Vector2(col - columns * 0.5f + 0.5f, row - rows * 0.5f + 0.5f);
            Update();
            return;
        }
        texture = new Texture2D(2, 2) { name = "Fog mask", filterMode = FilterMode.Bilinear };
        texture.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
        texture.Apply();
        sprite = Sprite.Create(texture, new Rect(0f, 0f, 2f, 2f), Vector2.one * 0.5f, 2f);
        for (int row = 0; row < rows; row++)
        for (int col = 0; col < columns; col++)
        {
            int index = row * columns + col;
            GameObject cell = new GameObject("Darkness veil");
            cell.transform.SetParent(transform, false);
            cell.transform.localPosition = new Vector2(col - columns * 0.5f + 0.5f, row - rows * 0.5f + 0.5f);
            cell.transform.localScale = new Vector3(1.015f, 1.015f, 1f);
            SpriteRenderer renderer = cell.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = 80;
            cells[index] = renderer;
            positions[index] = cell.transform.position;
        }
    }
    private void Update()
    {
        if (cells == null || player == null) return;
        if (smoothMask != null)
        {
            smoothMask.SetVector("_Pirate", player.transform.position);
            Vector3 bird = player.ScoutTransform != null ? player.ScoutTransform.position : player.transform.position;
            smoothMask.SetVector("_Bird", new Vector4(bird.x, bird.y, player.Scout != null ? player.Scout.RevealRadius : 1.8f, 0f));
            smoothMask.SetFloat("_HasBird", player.HasParrot ? 1f : 0f);
        }
        RevealedCellCount = 0;
        for (int i = 0; i < cells.Length; i++)
        {
            float pirateLight = Mathf.Clamp01((Vector2.Distance(positions[i], player.transform.position) - 1.3f) / 1.4f);
            float birdLight = player.HasParrot && player.ScoutTransform != null
                ? Mathf.Clamp01((Vector2.Distance(positions[i], player.ScoutTransform.position) - player.Scout.RevealRadius + 1f) / 1.5f)
                : 1f;
            float darkness = Mathf.Min(pirateLight, birdLight);
            if (darkness < 0.3f) RevealedCellCount++;
            if (cells[i] != null) cells[i].color = new Color(0.008f, 0.016f, 0.027f, darkness * MaximumOpacity);
        }
    }
    private void OnDestroy()
    {
        if (sprite != null) Destroy(sprite);
        if (texture != null) Destroy(texture);
        if (smoothMask != null) Destroy(smoothMask);
        if (maskMesh != null) Destroy(maskMesh);
    }
}
