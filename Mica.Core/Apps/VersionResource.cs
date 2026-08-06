using System.Text;

namespace Mica.Core.Apps;

/// <summary>
/// Builds a VS_VERSIONINFO block naming an executable after the app it runs.
/// <para>
/// Replacing the icon is only half of an app's identity. Shells that key on the
/// executable read its <c>FileDescription</c> for the label, so a copy of the host
/// that keeps the host's version resource is still called "Mica.Host" no matter what
/// the file is named or which icon it carries.
/// </para>
/// <para>
/// The format is a tree of length-prefixed nodes, every one aligned to a 32-bit
/// boundary measured from the start of the resource. Lengths cover the node and its
/// children, so they are back-patched once the children are written.
/// </para>
/// </summary>
internal static class VersionResource
{
    private const ushort TextValue = 1;
    private const ushort BinaryValue = 0;

    /// <summary>
    /// US English, Unicode. The Var block stores it as a DWORD whose low word is the
    /// language, while the string table names itself language-then-codepage — the two
    /// are byte-reversed relative to each other, so they cannot share one constant.
    /// </summary>
    private const uint Translation = 0x04B00409;

    private const string StringTableKey = "040904B0";

    public static byte[] Build(string appName, string fileName, string siteHost)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Unicode, leaveOpen: true);

        WriteNode(writer, "VS_VERSION_INFO", FixedFileInfo(), 52, BinaryValue, w =>
        {
            WriteNode(w, "StringFileInfo", null, 0, TextValue, sfi =>
            {
                WriteNode(sfi, StringTableKey, null, 0, TextValue, table =>
                {
                    WriteString(table, "CompanyName", MicaPaths.ProductName);
                    WriteString(table, "FileDescription", appName);
                    WriteString(table, "FileVersion", "1.0.0.0");
                    WriteString(table, "InternalName", fileName);
                    WriteString(table, "OriginalFilename", fileName);
                    WriteString(table, "ProductName", appName);
                    WriteString(table, "ProductVersion", "1.0.0.0");
                    WriteString(table, "Comments", $"{appName} — {siteHost}, built with {MicaPaths.ProductName}");
                });
            });

            WriteNode(w, "VarFileInfo", null, 0, TextValue, vfi =>
            {
                WriteNode(vfi, "Translation", BitConverter.GetBytes(Translation), 4, BinaryValue, null);
            });
        });

        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] FixedFileInfo()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write(0xFEEF04BDu);  // signature
        writer.Write(0x00010000u);  // struct version
        writer.Write(0x00010000u);  // file version MS (1.0)
        writer.Write(0x00000000u);  // file version LS
        writer.Write(0x00010000u);  // product version MS
        writer.Write(0x00000000u);  // product version LS
        writer.Write(0x0000003Fu);  // flags mask
        writer.Write(0x00000000u);  // flags
        writer.Write(0x00000004u);  // VOS__WINDOWS32
        writer.Write(0x00000001u);  // VFT_APP
        writer.Write(0x00000000u);  // subtype
        writer.Write(0x00000000u);  // date MS
        writer.Write(0x00000000u);  // date LS

        writer.Flush();
        return stream.ToArray();
    }

    private static void WriteString(BinaryWriter writer, string key, string value)
    {
        // Text values measure their length in characters, including the terminator.
        var bytes = Encoding.Unicode.GetBytes(value + '\0');
        WriteNode(writer, key, bytes, value.Length + 1, TextValue, null);
    }

    private static void WriteNode(
        BinaryWriter writer,
        string key,
        byte[]? value,
        int valueLength,
        ushort type,
        Action<BinaryWriter>? children)
    {
        Align(writer);
        var start = writer.BaseStream.Position;

        writer.Write((ushort)0);              // back-patched below
        writer.Write((ushort)valueLength);
        writer.Write(type);
        writer.Write(Encoding.Unicode.GetBytes(key + '\0'));

        if (value is not null)
        {
            Align(writer);
            writer.Write(value);
        }

        children?.Invoke(writer);

        var end = writer.BaseStream.Position;
        writer.BaseStream.Position = start;
        writer.Write((ushort)(end - start));
        writer.BaseStream.Position = end;
    }

    private static void Align(BinaryWriter writer)
    {
        while (writer.BaseStream.Position % 4 != 0)
        {
            writer.Write((byte)0);
        }
    }
}
