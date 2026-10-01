using System.ComponentModel;
using System.Runtime.InteropServices;
using DoriDeck.Services;

namespace DoriDeck.Services.Windows;

internal sealed class WindowsKeyboardService : IKeyboardService
{
    private const uint ExtendedKey = 0x0001;
    private const uint KeyUp = 0x0002;
    private const uint Unicode = 0x0004;

    public void Press(DoriDeckKey key)
    {
        var code = Map(key);
        var flags = ExtendedFlag(code);
        Send(Key(code, flags), Key(code, flags | KeyUp));
    }

    public void PressChord(DoriDeckKey modifier, DoriDeckKey key)
    {
        var modifierCode = Map(modifier);
        var keyCode = Map(key);
        var modifierFlags = ExtendedFlag(modifierCode);
        var keyFlags = ExtendedFlag(keyCode);
        Send(Key(modifierCode, modifierFlags), Key(keyCode, keyFlags),
            Key(keyCode, keyFlags | KeyUp), Key(modifierCode, modifierFlags | KeyUp));
    }

    public void EnterText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        foreach (var character in text)
        {
            Send(Key(character, Unicode), Key(character, Unicode | KeyUp));
        }
    }

    public bool TryRelease(DoriDeckKey key)
    {
        try
        {
            var code = Map(key);
            Send(Key(code, ExtendedFlag(code) | KeyUp));
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

    private static Input Key(ushort code, uint flags) => new()
    {
        Type = 1, // INPUT_KEYBOARD
        Data = new InputData { Keyboard = new KeyboardInput
        {
            VirtualKey = (flags & Unicode) == 0 ? code : (ushort)0,
            ScanCode = (flags & Unicode) != 0 ? code : (ushort)0,
            Flags = flags
        } }
    };

    private static void Send(params Input[] inputs)
    {
        if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) != inputs.Length)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    private static ushort Map(DoriDeckKey key) => key switch
    {
        DoriDeckKey.Return => 0x0D,
        DoriDeckKey.Escape => 0x1B,
        DoriDeckKey.UpArrow => 0x26,
        DoriDeckKey.DownArrow => 0x28,
        DoriDeckKey.LeftArrow => 0x25,
        DoriDeckKey.RightArrow => 0x27,
        DoriDeckKey.A => 0x41,
        DoriDeckKey.C => 0x43,
        DoriDeckKey.Shift => 0x10,
        DoriDeckKey.Control or DoriDeckKey.PrimaryModifier => 0x11,
        DoriDeckKey.LeftShift => 0xA0,
        DoriDeckKey.RightShift => 0xA1,
        DoriDeckKey.LeftAlt => 0xA4,
        DoriDeckKey.RightAlt => 0xA5,
        DoriDeckKey.LeftMeta => 0x5B,
        DoriDeckKey.RightMeta => 0x5C,
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, null),
    };

    // Windows synthesizes the "gray" arrow / nav-cluster / right-modifier / Windows keys
    // as extended scan codes; without this flag SendInput produces the numpad-key variant
    // instead, which some apps (raw input / low-level hooks) don't treat as the same key.
    private static uint ExtendedFlag(ushort code) => IsExtendedKey(code) ? ExtendedKey : 0;

    private static bool IsExtendedKey(ushort code) => code switch
    {
        0x25 or 0x26 or 0x27 or 0x28 => true, // arrows
        0xA5 => true, // right alt
        0x5B or 0x5C => true, // left/right Windows key
        _ => false,
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputData Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputData
    {
        [FieldOffset(0)] public KeyboardInput Keyboard;
        [FieldOffset(0)] public MouseInput Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, Input[] inputs, int size);
}
