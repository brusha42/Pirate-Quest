using System;
using System.Globalization;
using UnityEngine;

public static class PirateScorePresentation
{
    private static GUIStyle title, score, label, right, footnote;

    public static void Draw(PirateRunStatistics statistics, PirateTreasureLedger ledger, int deaths)
    {
        EnsureStyles();
        PirateRunStatistics.ScoreBreakdown result = statistics.Evaluate(ledger, deaths, true);
        Matrix4x4 oldMatrix = GUI.matrix;
        Color oldColor = GUI.color;
        float scale = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
        try
        {
            Rect panel = new Rect(Screen.width / scale * .5f - 310f, Screen.height / scale * .5f - 207f, 620f, 414f);
            Fill(panel, new Color(.025f, .035f, .045f, .97f));
            Fill(new Rect(panel.x, panel.y, panel.width, 2f), new Color(.85f, .63f, .25f));
            GUI.color = Color.white;
            GUI.Label(new Rect(panel.x + 24f, panel.y + 18f, 572f, 34f), "LETTER DELIVERED", title);
            GUI.Label(new Rect(panel.x + 24f, panel.y + 53f, 572f, 25f), "The king seals the sea gates. The tide falls.", footnote);
            GUI.Label(new Rect(panel.x + 24f, panel.y + 84f, 572f, 56f), Number(result.Total) + " / 100,000", score);
            Row(panel, 158f, "Treasure value", Number(result.TreasureValue) + " / " + Number(result.PossibleTreasureValue), result.Treasure, 60000);
            Row(panel, 199f, "Time", FormatTime(statistics.ActiveSeconds), result.Speed, 20000);
            Row(panel, 240f, "Enemies defeated", result.Defeats + " / " + result.PossibleDefeats, result.Combat, 15000);
            Row(panel, 281f, "Deaths", deaths.ToString(CultureInfo.InvariantCulture), result.Survival, 5000);
            GUI.Label(new Rect(panel.x + 24f, panel.y + 331f, 572f, 28f), result.HasFullHistory
                ? "Explore, fight and find a faster route."
                : "Imported save: partial stats. Start a new run for a full score.", footnote);
            GUI.Label(new Rect(panel.x + 24f, panel.y + 370f, 572f, 25f), "Esc  Menu", footnote);
        }
        finally { GUI.matrix = oldMatrix; GUI.color = oldColor; }
    }

    public static string FormatTime(double seconds)
    {
        TimeSpan time = TimeSpan.FromSeconds(Math.Max(0d, Math.Min(PirateRunStatistics.MaximumPlaySeconds, seconds)));
        return time.TotalHours >= 1d ? $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}" :
            $"{time.Minutes:00}:{time.Seconds:00}";
    }

    private static void Row(Rect panel, float y, string heading, string detail, int points, int maximum)
    {
        GUI.Label(new Rect(panel.x + 28f, panel.y + y, 195f, 25f), heading, label);
        GUI.Label(new Rect(panel.x + 222f, panel.y + y, 190f, 25f), detail, footnote);
        GUI.Label(new Rect(panel.x + 410f, panel.y + y, 182f, 25f), Number(points) + " / " + Number(maximum), right);
    }

    private static string Number(int value) => value.ToString("N0", CultureInfo.InvariantCulture);
    private static void Fill(Rect rect, Color color) { GUI.color = color; GUI.DrawTexture(rect, Texture2D.whiteTexture); }
    private static void EnsureStyles()
    {
        if (title != null) return;
        label = new GUIStyle(GUI.skin.label) { fontSize = 17, alignment = TextAnchor.MiddleLeft,
            normal = { textColor = new Color(.87f, .89f, .84f) } };
        PirateFrontEnd.KeepLabelStatesIdentical(label);
        title = new GUIStyle(label) { fontSize = 25, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        score = new GUIStyle(title) { fontSize = 39, normal = { textColor = new Color(1f, .79f, .38f) } };
        PirateFrontEnd.KeepLabelStatesIdentical(score);
        right = new GUIStyle(label) { alignment = TextAnchor.MiddleRight };
        footnote = new GUIStyle(label) { fontSize = 13, alignment = TextAnchor.MiddleCenter };
    }
}
