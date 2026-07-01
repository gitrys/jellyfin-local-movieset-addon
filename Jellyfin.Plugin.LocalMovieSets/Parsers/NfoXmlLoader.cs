using System;
using System.IO;
using System.Xml;
using System.Xml.Linq;

namespace Jellyfin.Plugin.LocalMovieSets.Parsers;

/// <summary>
/// Loads NFO files as XML, tolerating the trailing junk many scrapers append
/// after the closing root tag (e.g. a trailer URL on its own line), which
/// Kodi accepts but <see cref="XDocument.Load(string)"/> rejects.
/// </summary>
internal static class NfoXmlLoader
{
    /// <summary>
    /// Loads the given NFO file. If strict parsing fails, retries with the
    /// content truncated after the last closing tag.
    /// </summary>
    /// <param name="nfoPath">Path to the NFO file.</param>
    /// <returns>The parsed document.</returns>
    /// <exception cref="XmlException">Thrown when the file is not recoverable XML.</exception>
    public static XDocument Load(string nfoPath)
    {
        try
        {
            return XDocument.Load(nfoPath, LoadOptions.None);
        }
        catch (XmlException)
        {
            var text = File.ReadAllText(nfoPath);

            // Truncate everything after the last closing tag ("</root>").
            var lastCloseStart = text.LastIndexOf("</", StringComparison.Ordinal);
            if (lastCloseStart < 0)
            {
                throw;
            }

            var lastCloseEnd = text.IndexOf('>', lastCloseStart);
            if (lastCloseEnd < 0)
            {
                throw;
            }

            return XDocument.Parse(text[..(lastCloseEnd + 1)], LoadOptions.None);
        }
    }
}
