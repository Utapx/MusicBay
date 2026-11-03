using System;

namespace Common
{
    public static class VersionCompare
    {
        /// <summary>
        /// True if the release tag (such as "v0.2.0") is a higher version than the installed one.
        /// A tag that is not a plain version is never treated as an update.
        /// </summary>
        public static bool IsNewer(string? releaseTag, string? installed)
        {
            if (!TryParse(releaseTag, out var release) || !TryParse(installed, out var current))
            {
                return false;
            }
            return release > current;
        }

        private static bool TryParse(string? text, out Version version)
        {
            version = new Version();
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }
            string trimmed = text!.Trim();
            if (trimmed.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed.Substring(1);
            }
            // "0.2" and "0.2.0" must compare equal, but Version treats a missing part as lower
            if (!Version.TryParse(trimmed.Contains(".") ? trimmed : trimmed + ".0", out var parsed))
            {
                return false;
            }
            version = new Version(parsed.Major, parsed.Minor, Math.Max(parsed.Build, 0));
            return true;
        }
    }
}
