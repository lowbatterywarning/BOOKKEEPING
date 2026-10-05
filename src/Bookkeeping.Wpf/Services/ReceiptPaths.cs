using System.IO;

namespace Bookkeeping.Wpf.Services;

public static class ReceiptPaths
{
    public static string Resolve(string attachmentsRoot, string relativePath)
    {
        var parts = relativePath.Replace('\\', '/').Split('/');
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath)
            || parts.Any(p => string.IsNullOrWhiteSpace(p) || p is "." or ".."
                || p.EndsWith('.') || p.EndsWith(' ') || p.Contains(':')
                || p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            throw new InvalidDataException("The receipt path is unsafe.");
        var root = Path.GetFullPath(attachmentsRoot).TrimEnd(Path.DirectorySeparatorChar);
        var result = Path.GetFullPath(Path.Combine(root, Path.Combine(parts)));
        if (!result.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The receipt is outside the attachments folder.");
        // A junction or symbolic link must not bypass the containment check.
        var current = root;
        foreach (var part in new[] { "" }.Concat(parts))
        {
            if (part.Length > 0) current = Path.Combine(current, part);
            if ((File.Exists(current) || Directory.Exists(current))
                && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Receipt paths cannot contain symbolic links or junctions.");
        }
        return result;
    }
}
