using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CustomContentCore
{
    /// <summary>Checks content files and paths received from other players before they're stored or used.</summary>
    internal static class ContentValidator
    {
        /// <summary>The largest image dimension accepted.</summary>
        public const int MaxImageSide = 8192;

        /// <summary>The most pixels accepted in one image (limits memory use when decoding). A 4x farmer pants sheet is 7680x5504 (42 MP).</summary>
        public const long MaxImagePixels = (long)MaxImageSide * MaxImageSide;

        /// <summary>The largest JSON file accepted.</summary>
        public const int MaxJsonBytes = 2 * 1024 * 1024;

        /// <summary>The longest text value accepted in a JSON file.</summary>
        public const int MaxJsonStringLength = 20_000;

        private static readonly Regex SafePath = new(@"^[A-Za-z0-9 _\-\.\(\)]+(/[A-Za-z0-9 _\-\.\(\)]+)*$", RegexOptions.Compiled);

        private static readonly string[] ReservedWindowsNames = { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "CONIN$", "CONOUT$", "CLOCK$" };

        /// <summary>Whether a relative path (with '/' separators) is safe on every OS: plain characters only, no '.'/'..' segments, no device names, reasonable length.</summary>
        public static bool IsSafeRelativePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || path.Length > 200 || !SafePath.IsMatch(path))
                return false;
            foreach (string segment in path.Split('/'))
            {
                if (segment is "." or ".." || segment.EndsWith('.') || segment.EndsWith(' ') || segment.StartsWith(' '))
                    return false;
                string stem = segment.Split('.')[0].Trim();
                if (ReservedWindowsNames.Contains(stem, StringComparer.OrdinalIgnoreCase))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Rebuild a received file from scratch, so only its actual content survives: images are decoded (in managed code, within
        /// the size limits) and re-encoded as a plain PNG; JSON is parsed and written out again without comments or metadata.
        /// </summary>
        /// <param name="extension">The file extension (lowercase, with dot). Images of any allowed extension must be sent as PNG data.</param>
        /// <param name="bytes">The received file.</param>
        /// <param name="clean">The rebuilt file to store.</param>
        /// <param name="error">Why it was rejected.</param>
        public static bool TrySanitize(string extension, byte[] bytes, out byte[] clean, out string error)
        {
            clean = Array.Empty<byte>();
            try
            {
                switch (extension)
                {
                    case ".png":
                    case ".jpg":
                    case ".jpeg":
                        clean = SafePng.Encode(SafePng.Decode(bytes, MaxImageSide, MaxImagePixels));
                        break;
                    case ".json":
                        clean = RewriteJson(bytes);
                        break;
                    default:
                        error = "file type not allowed";
                        return false;
                }
            }
            catch (Exception ex) // anything unexpected means the file is rejected
            {
                error = ex.Message;
                return false;
            }
            error = "";
            return true;
        }

        /// <summary>Whether a file path is inside a folder, after resolving '..' and links to the real location.</summary>
        public static bool IsInsideFolder(string path, string folder)
        {
            try
            {
                string realFolder = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string real = Path.GetFullPath(path);
                FileInfo file = new(real);
                if (file.Exists && file.LinkTarget != null)
                    return false; // don't follow links
                return real.StartsWith(realFolder, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }


        /*********
        ** Private methods
        *********/
        /// <summary>Parse JSON and write it out again: UTF-8, an object at the root, limited depth and string length, and no '$' metadata properties.</summary>
        private static byte[] RewriteJson(byte[] bytes)
        {
            if (bytes.Length > MaxJsonBytes)
                throw new InvalidDataException("JSON file too large");
            string text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
            using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(text.TrimStart('\uFEFF'), new System.Text.Json.JsonDocumentOptions
            {
                CommentHandling = System.Text.Json.JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
                MaxDepth = 32
            });
            if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
                throw new InvalidDataException("JSON must be an object");

            using MemoryStream output = new();
            using (System.Text.Json.Utf8JsonWriter writer = new(output, new System.Text.Json.JsonWriterOptions { Indented = true }))
                WriteJson(writer, document.RootElement);
            return output.ToArray();
        }

        private static void WriteJson(System.Text.Json.Utf8JsonWriter writer, System.Text.Json.JsonElement element)
        {
            switch (element.ValueKind)
            {
                case System.Text.Json.JsonValueKind.Object:
                    writer.WriteStartObject();
                    foreach (System.Text.Json.JsonProperty property in element.EnumerateObject())
                    {
                        if (property.Name.StartsWith('$') || property.Name.Length > 200)
                            continue; // no Newtonsoft metadata like $type/$ref
                        writer.WritePropertyName(property.Name);
                        WriteJson(writer, property.Value);
                    }
                    writer.WriteEndObject();
                    break;
                case System.Text.Json.JsonValueKind.Array:
                    writer.WriteStartArray();
                    foreach (System.Text.Json.JsonElement item in element.EnumerateArray())
                        WriteJson(writer, item);
                    writer.WriteEndArray();
                    break;
                case System.Text.Json.JsonValueKind.String:
                    string value = element.GetString() ?? "";
                    if (value.Length > MaxJsonStringLength)
                        throw new InvalidDataException("JSON text value too long");
                    writer.WriteStringValue(value);
                    break;
                case System.Text.Json.JsonValueKind.Number:
                    if (element.TryGetInt64(out long whole))
                        writer.WriteNumberValue(whole);
                    else if (element.TryGetDouble(out double number) && double.IsFinite(number))
                        writer.WriteNumberValue(number);
                    else
                        throw new InvalidDataException("invalid JSON number");
                    break;
                case System.Text.Json.JsonValueKind.True:
                case System.Text.Json.JsonValueKind.False:
                    writer.WriteBooleanValue(element.GetBoolean());
                    break;
                default:
                    writer.WriteNullValue();
                    break;
            }
        }
    }
}
