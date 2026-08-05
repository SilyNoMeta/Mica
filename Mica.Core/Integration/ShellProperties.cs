using System.Runtime.InteropServices;

namespace Mica.Core.Integration;

/// <summary>PROPERTYKEY — identifies one property in a shell property store.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct PropertyKey
{
    public Guid FormatId;
    public uint PropertyId;

    /// <summary>PKEY_AppUserModel_ID</summary>
    public static PropertyKey AppUserModelId => new()
    {
        FormatId = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
        PropertyId = 5,
    };
}

/// <summary>
/// A string PROPVARIANT, built by hand: the SDK's InitPropVariantFromString is an
/// inline header helper, not an export anything can be P/Invoked into.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PropVariant
{
    private const ushort VtLpwstr = 31;

    private ushort _type;
    private ushort _reserved1;
    private ushort _reserved2;
    private ushort _reserved3;
    private IntPtr _value;
    private IntPtr _padding;

    public static PropVariant FromString(string value) => new()
    {
        _type = VtLpwstr,
        _value = Marshal.StringToCoTaskMemUni(value),
    };
}

[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
internal interface IPropertyStore
{
    void GetCount(out uint count);

    void GetAt(uint index, out PropertyKey key);

    void GetValue(ref PropertyKey key, out PropVariant value);

    void SetValue(ref PropertyKey key, ref PropVariant value);

    void Commit();
}

internal static class ShellProperties
{
    [DllImport("ole32.dll")]
    internal static extern int PropVariantClear(ref PropVariant variant);

    /// <summary>Writes a string property and commits it.</summary>
    internal static void SetString(IPropertyStore store, PropertyKey key, string value)
    {
        var variant = PropVariant.FromString(value);
        try
        {
            store.SetValue(ref key, ref variant);
            store.Commit();
        }
        finally
        {
            PropVariantClear(ref variant);
        }
    }
}
