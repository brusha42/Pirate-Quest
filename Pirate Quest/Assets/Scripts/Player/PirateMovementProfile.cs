public static class PirateMovementProfile
{
    public static float ForChapter(int chapter)
    {
        if (chapter <= 0) return 6f;
        if (chapter == 1) return 6.6f;
        if (chapter == 2) return 7.3f;
        return 8f;
    }
}
