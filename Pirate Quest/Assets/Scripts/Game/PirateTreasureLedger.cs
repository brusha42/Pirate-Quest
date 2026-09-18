using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

public sealed class PirateTreasureLedger
{
    public const int MaximumReceipts = 1024;
    private static readonly Regex ValidKey = new Regex(@"\At1:[0-3]:[0-9A-F]{16}:[0-9]{1,4}:[0-2]\z", RegexOptions.CultureInvariant);
    private readonly HashSet<string> receipts = new HashSet<string>(StringComparer.Ordinal);
    public int Score { get; private set; }
    public int Count => receipts.Count;
    public bool Contains(string key) => key != null && receipts.Contains(key);
    public static bool IsValidKey(string key) => key != null && key.Length <= 40 && ValidKey.IsMatch(key);
    public static int ValueOf(string key)
    {
        if (!IsValidKey(key)) throw new ArgumentException("Invalid treasure receipt.", nameof(key));
        return key[key.Length - 1] == '0' ? 10 : key[key.Length - 1] == '1' ? 40 : 150;
    }
    public bool Collect(string key)
    {
        if (!IsValidKey(key) || receipts.Count >= MaximumReceipts) return false;
        if (!receipts.Add(key)) return false;
        Score += ValueOf(key);
        return true;
    }
    public void Clear() { receipts.Clear(); Score = 0; }
    public string[] Capture() => receipts.OrderBy(key => key, StringComparer.Ordinal).ToArray();
    public void Restore(string[] saved)
    {
        if (saved != null && (saved.Length > MaximumReceipts || saved.Any(key => !IsValidKey(key)) ||
            saved.Distinct(StringComparer.Ordinal).Count() != saved.Length))
            throw new ArgumentException("Invalid saved treasure receipts.", nameof(saved));
        Clear();
        if (saved != null) foreach (string key in saved) Collect(key);
    }
}
