using System.Runtime.InteropServices;
using DoriDeck.Services;

namespace DoriDeck.Services.MacOS;

/// <summary>
/// Keyboard injection through CoreGraphics.
/// Requires Accessibility permission for the plugin host/process.
/// </summary>
internal sealed class MacOSKeyboardService : IKeyboardService
{
    private const string ApplicationServicesLibrary =
        "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";

    private const string CoreFoundationLibrary =
        "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    private const int CGHidEventTap = 0;
    private const int EventSourceStateHidSystemState = 1;

    private const long FlagMaskShift = 1L << 17;
    private const long FlagMaskControl = 1L << 18;
    private const long FlagMaskAlternate = 1L << 19;
    private const long FlagMaskCommand = 1L << 20;

    [DllImport(ApplicationServicesLibrary)]
    private static extern IntPtr CGEventSourceCreate(int stateId);

    [DllImport(ApplicationServicesLibrary)]
    private static extern IntPtr CGEventCreateKeyboardEvent(
        IntPtr source,
        ushort virtualKey,
        [MarshalAs(UnmanagedType.I1)] bool keyDown);

    [DllImport(ApplicationServicesLibrary)]
    private static extern void CGEventSetFlags(IntPtr eventRef, long flags);

    [DllImport(ApplicationServicesLibrary)]
    private static extern void CGEventPost(int tap, IntPtr eventRef);

    [DllImport(ApplicationServicesLibrary)]
    private static extern void CGEventKeyboardSetUnicodeString(
        IntPtr eventRef,
        int stringLength,
        ushort[] unicodeString);

    [DllImport(CoreFoundationLibrary)]
    private static extern void CFRelease(IntPtr cf);

    public void Press(DoriDeckKey key)
    {
        PostKeyStroke(MapKeyCode(key), flags: 0);
    }

    public void PressChord(DoriDeckKey modifier, DoriDeckKey key)
    {
        PostKeyStroke(MapKeyCode(key), MapModifierFlag(modifier));
    }

    public void EnterText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (text.Length == 0)
        {
            return;
        }

        var unicodeChars = text.ToCharArray();
        var source = CreateEventSource();

        try
        {
            PostUnicodeEvent(source, unicodeChars, keyDown: true);
            PostUnicodeEvent(source, unicodeChars, keyDown: false);
        }
        finally
        {
            CFRelease(source);
        }
    }

    public bool TryRelease(DoriDeckKey key)
    {
        try
        {
            var source = CreateEventSource();

            try
            {
                PostKeyEvent(MapKeyCode(key), keyDown: false, flags: 0, source);
            }
            finally
            {
                CFRelease(source);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public void ReleaseModifiersSafely()
    {
        TryRelease(DoriDeckKey.Control);
        TryRelease(DoriDeckKey.LeftShift);
        TryRelease(DoriDeckKey.RightShift);
        TryRelease(DoriDeckKey.LeftAlt);
        TryRelease(DoriDeckKey.RightAlt);
        TryRelease(DoriDeckKey.LeftMeta);
        TryRelease(DoriDeckKey.RightMeta);
    }

    private static IntPtr CreateEventSource()
    {
        var source = CGEventSourceCreate(EventSourceStateHidSystemState);

        if (source == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                "Unable to create a CoreGraphics keyboard event source.");
        }

        return source;
    }

    private static void PostKeyStroke(ushort keyCode, long flags)
    {
        var source = CreateEventSource();

        try
        {
            PostKeyEvent(keyCode, keyDown: true, flags, source);
            PostKeyEvent(keyCode, keyDown: false, flags, source);
        }
        finally
        {
            CFRelease(source);
        }
    }

    private static void PostKeyEvent(
        ushort keyCode,
        bool keyDown,
        long flags,
        IntPtr source)
    {
        var keyEvent = CGEventCreateKeyboardEvent(source, keyCode, keyDown);

        if (keyEvent == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                "Unable to create a CoreGraphics keyboard event.");
        }

        try
        {
            // keyboard fix, triggers shortcuts otherwise (like Ctrl+M in replacements:))
            CGEventSetFlags(keyEvent, flags);

            CGEventPost(CGHidEventTap, keyEvent);
        }
        finally
        {
            CFRelease(keyEvent);
        }
    }

    private static void PostUnicodeEvent(
        IntPtr source,
        char[] unicodeChars,
        bool keyDown)
    {
        var keyEvent = CGEventCreateKeyboardEvent(source, virtualKey: 0, keyDown);

        if (keyEvent == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                "Unable to create a CoreGraphics Unicode keyboard event.");
        }

        try
        {
            // keyboard fix
            CGEventSetFlags(keyEvent, 0);

            var buffer = new ushort[unicodeChars.Length];
            for (var i = 0; i < unicodeChars.Length; i++)
            {
                buffer[i] = unicodeChars[i];
            }

            CGEventKeyboardSetUnicodeString(keyEvent, buffer.Length, buffer);
            CGEventPost(CGHidEventTap, keyEvent);
        }
        finally
        {
            CFRelease(keyEvent);
        }
    }

    private static long MapModifierFlag(DoriDeckKey modifier) => modifier switch
    {
        DoriDeckKey.PrimaryModifier => FlagMaskCommand,
        DoriDeckKey.Control => FlagMaskControl,
        DoriDeckKey.Shift or DoriDeckKey.LeftShift or DoriDeckKey.RightShift => FlagMaskShift,
        DoriDeckKey.LeftAlt or DoriDeckKey.RightAlt => FlagMaskAlternate,
        DoriDeckKey.LeftMeta or DoriDeckKey.RightMeta => FlagMaskCommand,
        _ => throw new ArgumentOutOfRangeException(
            nameof(modifier), modifier, "Not a modifier key."),
    };

    // macOS virtual key codes (kVK_* from HIToolbox/Events.h).
    private static ushort MapKeyCode(DoriDeckKey key) => key switch
    {
        DoriDeckKey.Return => 0x24,
        DoriDeckKey.Escape => 0x35,
        DoriDeckKey.UpArrow => 0x7E,
        DoriDeckKey.DownArrow => 0x7D,
        DoriDeckKey.LeftArrow => 0x7B,
        DoriDeckKey.RightArrow => 0x7C,
        DoriDeckKey.A => 0x00,
        DoriDeckKey.C => 0x08,
        DoriDeckKey.Shift => 0x38,
        DoriDeckKey.Control => 0x3B,
        DoriDeckKey.LeftShift => 0x38,
        DoriDeckKey.RightShift => 0x3C,
        DoriDeckKey.LeftAlt => 0x3A,
        DoriDeckKey.RightAlt => 0x3D,
        DoriDeckKey.LeftMeta => 0x37,
        DoriDeckKey.RightMeta => 0x36,
        DoriDeckKey.PrimaryModifier => 0x37,
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, null),
    };
}
