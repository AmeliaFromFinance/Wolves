"""
Drag a file onto this script:
  - plain file              -> ZLIB compress + Base64 encode -> clipboard
  - ZLIB + Base64 text file -> Base64 decode + ZLIB decompress -> clipboard
Windows only (clipboard via WinAPI), no third-party packages.
"""

import base64
import binascii
import ctypes
import ctypes.wintypes
import os
import sys
import time
import zlib


CF_UNICODETEXT = 13
GMEM_MOVEABLE = 0x0002
ZLIB_LEVEL = 9
WBITS_AUTO = 47  # zlib or gzip header, detected automatically


def SetClipboardText(text):
    user32 = ctypes.WinDLL("user32", use_last_error=True)
    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)

    # Explicit signatures: handles are 64-bit pointers on x64 Python
    user32.OpenClipboard.argtypes = [ctypes.wintypes.HWND]
    user32.OpenClipboard.restype = ctypes.wintypes.BOOL
    user32.EmptyClipboard.restype = ctypes.wintypes.BOOL
    user32.SetClipboardData.argtypes = [ctypes.wintypes.UINT, ctypes.wintypes.HANDLE]
    user32.SetClipboardData.restype = ctypes.wintypes.HANDLE
    user32.CloseClipboard.restype = ctypes.wintypes.BOOL
    kernel32.GlobalAlloc.argtypes = [ctypes.wintypes.UINT, ctypes.c_size_t]
    kernel32.GlobalAlloc.restype = ctypes.wintypes.HGLOBAL
    kernel32.GlobalLock.argtypes = [ctypes.wintypes.HGLOBAL]
    kernel32.GlobalLock.restype = ctypes.c_void_p
    kernel32.GlobalUnlock.argtypes = [ctypes.wintypes.HGLOBAL]
    kernel32.GlobalFree.argtypes = [ctypes.wintypes.HGLOBAL]

    data = text.encode("utf-16-le") + b"\x00\x00"

    # Another app may hold the clipboard for a moment
    for attempt in range(10):
        if user32.OpenClipboard(None):
            break
        time.sleep(0.1)
    else:
        raise OSError("Could not open the clipboard.")

    try:
        user32.EmptyClipboard()

        handle = kernel32.GlobalAlloc(GMEM_MOVEABLE, len(data))
        if not handle:
            raise MemoryError("GlobalAlloc failed.")

        pointer = kernel32.GlobalLock(handle)
        ctypes.memmove(pointer, data, len(data))
        kernel32.GlobalUnlock(handle)

        if not user32.SetClipboardData(CF_UNICODETEXT, handle):
            kernel32.GlobalFree(handle)
            raise OSError("SetClipboardData failed.")
    finally:
        user32.CloseClipboard()


def TryDecode(raw):
    """Returns decompressed bytes if raw is Base64 of ZLIB data, otherwise None."""
    try:
        text = raw.decode("ascii")
    except UnicodeDecodeError:
        return None

    compact = "".join(text.split())
    if not compact:
        return None

    try:
        compressed = base64.b64decode(compact, validate=True)
        return zlib.decompress(compressed, WBITS_AUTO)
    except (binascii.Error, ValueError, zlib.error):
        return None


def Encode(raw):
    return base64.b64encode(zlib.compress(raw, ZLIB_LEVEL)).decode("ascii")


def Process(path):
    with open(path, "rb") as file:
        raw = file.read()

    decoded = TryDecode(raw)

    if decoded is None:
        result = Encode(raw)
        SetClipboardText(result)
        print(f"Encoded (ZLIB + Base64): {len(raw)} -> {len(result)} bytes. Copied to clipboard.")
        return

    try:
        text = decoded.decode("utf-8-sig")
    except UnicodeDecodeError:
        # Binary payload cannot go to the clipboard as text: save it next to the source
        outputPath = os.path.splitext(path)[0] + ".decoded.bin"
        with open(outputPath, "wb") as file:
            file.write(decoded)
        print(f"Decoded {len(decoded)} bytes of binary data (not text), saved to: {outputPath}")
        return

    SetClipboardText(text)
    print(f"Decoded (Base64 + ZLIB): {len(raw)} -> {len(decoded)} bytes. Copied to clipboard.")


def Main():
    if len(sys.argv) < 2:
        print("Drag a file onto ZLIB.py (or run: python ZLIB.py <file>).")
        return 1

    exitCode = 0
    for path in sys.argv[1:]:
        print(f"> {path}")
        try:
            Process(path)
        except Exception as error:
            print(f"[ERROR] {type(error).__name__}: {error}")
            exitCode = 1

    return exitCode


if __name__ == "__main__":
    code = Main()
    input("\nPress Enter to exit...")
    sys.exit(code)
