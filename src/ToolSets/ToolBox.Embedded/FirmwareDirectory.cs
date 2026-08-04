namespace ToolBox.Embedded;

/// <summary>
/// The directory <see cref="BuildTools"/> runs <c>make</c> in. Wrapped rather
/// than injecting a bare <see cref="string"/> so DI registration stays
/// unambiguous — same "wrap a primitive in a named type" shape as
/// <c>ToolsetDescriptor</c> in Core. Production points at
/// <c>firmware/nucleo-blink/</c> (see <see cref="EmbeddedToolsetExtensions"/>);
/// tests point at a small fixture directory instead, which is the whole
/// reason this is a constructor-injected value and not a hardcoded path.
/// </summary>
public sealed record FirmwareDirectory(string Path);
