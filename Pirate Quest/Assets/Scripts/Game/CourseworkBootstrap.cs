using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.SceneManagement;

public static class CourseworkBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Subscribe()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Install();

    public static void Install()
    {
        PlayerMovement player = Object.FindFirstObjectByType<PlayerMovement>();

        if (player == null)
        {
            Debug.LogError("CourseworkBootstrap: в сцене не найден PlayerMovement.");
            return;
        }

        if (player.GetComponent<PlayerVisualAnimator>() == null)
        {
            player.gameObject.AddComponent<PlayerVisualAnimator>();
        }

        if (player.GetComponent<PlayerAbilities>() == null)
        {
            player.gameObject.AddComponent<PlayerAbilities>();
        }

        if (player.GetComponent<PlayerLife>() == null)
        {
            player.gameObject.AddComponent<PlayerLife>();
        }

        if (player.GetComponent<PlayerGrapple>() == null)
        {
            player.gameObject.AddComponent<PlayerGrapple>();
        }

        TryAssignPlayerTag(player.gameObject);
        ConfigurePlayerRendering(player.gameObject);
        ConfigureSpikeTilemap();

        if (Object.FindFirstObjectByType<CampaignChapter>() == null &&
            !System.Array.Exists(System.Environment.GetCommandLineArgs(), value => value == "-pirateQuestTraversalTest"))
        {
            new GameObject("Campaign Chapter").AddComponent<CampaignChapter>();
        }

        if (Object.FindFirstObjectByType<PirateGameFlow>() == null)
        {
            new GameObject("Pirate Game Flow").AddComponent<PirateGameFlow>();
        }
    }

    private static void TryAssignPlayerTag(GameObject player)
    {
        try
        {
            player.tag = "Player";
        }
        catch (UnityException)
        {
            Debug.LogWarning("CourseworkBootstrap: тег Player отсутствует, используется поиск по компонентам.");
        }
    }

    private static void ConfigureSpikeTilemap()
    {
        GameObject spikeObject = GameObject.Find("SpikeTilemap");

        if (spikeObject == null || spikeObject.GetComponent<Tilemap>() == null)
        {
            return;
        }

        spikeObject.layer = 0;
        spikeObject.GetComponent<Tilemap>().ClearAllTiles();

        TilemapCollider2D collider = spikeObject.GetComponent<TilemapCollider2D>();
        if (collider == null)
        {
            collider = spikeObject.AddComponent<TilemapCollider2D>();
        }

        collider.isTrigger = true;

        if (spikeObject.GetComponent<InstantKillHazard>() == null)
        {
            spikeObject.AddComponent<InstantKillHazard>();
        }
    }

    private static void ConfigurePlayerRendering(GameObject player)
    {
        foreach (SpriteRenderer spriteRenderer in player.GetComponentsInChildren<SpriteRenderer>(true))
        {
            spriteRenderer.sortingOrder = Mathf.Max(spriteRenderer.sortingOrder, 10);
        }
    }
}
