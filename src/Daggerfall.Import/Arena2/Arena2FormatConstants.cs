namespace Daggerfall.Import.Arena2;

/// <summary>Fixed structural values for the supported Arena2 binary formats.</summary>
public static class Arena2FormatConstants
{
    /// <summary>BSA header byte count.</summary>
    public const int BsaHeaderBytes = 4;

    /// <summary>Named BSA directory entry byte count.</summary>
    public const int NamedBsaDirectoryEntryBytes = 18;

    /// <summary>Numeric BSA directory entry byte count.</summary>
    public const int NumericBsaDirectoryEntryBytes = 8;

    /// <summary>Named BSA directory marker.</summary>
    public const ushort NamedBsaDirectoryType = 0x0100;

    /// <summary>Numeric BSA directory marker.</summary>
    public const ushort NumericBsaDirectoryType = 0x0200;

    /// <summary>IMG header byte count.</summary>
    public const int ImgHeaderBytes = 12;

    /// <summary>Explicit headerless UI canvas width.</summary>
    public const ushort HeaderlessUiImgWidth = 320;

    /// <summary>Explicit headerless UI canvas height.</summary>
    public const ushort HeaderlessUiImgHeight = 200;

    /// <summary>Explicit headerless UI canvas byte count.</summary>
    public const int HeaderlessUiImgBytes = HeaderlessUiImgWidth * HeaderlessUiImgHeight;

    /// <summary>FNT glyph count.</summary>
    public const int FntGlyphCount = 240;

    /// <summary>FNT fixed-metrics header byte count.</summary>
    public const int FntHeaderBytes = 4;

    /// <summary>FNT source bytes per glyph.</summary>
    public const int FntGlyphBytes = 32;

    /// <summary>FNT glyph table byte count.</summary>
    public const int FntGlyphTableBytes = FntGlyphCount * 4;

    /// <summary>Palette RGB payload byte count.</summary>
    public const int PaletteRgbBytes = 768;

    /// <summary>Palette header plus RGB payload byte count.</summary>
    public const int PaletteHeaderedBytes = 776;
}
