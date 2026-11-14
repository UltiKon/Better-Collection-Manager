using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using UnityEngine;

namespace BetterCollectionManager
{
    internal static class DeckImageClipboard
    {
        internal static bool Supported => Application.platform == RuntimePlatform.WindowsPlayer
            || Application.platform == RuntimePlatform.OSXPlayer;

        // Return false only for a temporarily occupied Windows clipboard.
        internal static bool TryCopy(byte[] png, byte[] dib)
        {
            if (Application.platform == RuntimePlatform.OSXPlayer) { CopyMac(png); return true; }
            if (Application.platform != RuntimePlatform.WindowsPlayer)
                throw new PlatformNotSupportedException("Image clipboard export requires Windows, Wine/CrossOver or macOS.");
            uint format = RegisterClipboardFormat("PNG");
            if (format == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            IntPtr pngMemory = IntPtr.Zero, dibMemory = IntPtr.Zero;
            bool opened = false;
            try
            {
                // Supply a real owner so EmptyClipboard does not leave a null owner.
                IntPtr owner = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                if (owner == IntPtr.Zero) owner = GetActiveWindow();
                if (owner == IntPtr.Zero) throw new InvalidOperationException("The Hearthstone window is unavailable.");
                if (!OpenClipboard(owner)) return false;
                opened = true;
                pngMemory = Allocate(png); dibMemory = Allocate(dib);
                if (!EmptyClipboard()) throw new Win32Exception(Marshal.GetLastWin32Error());
                Put(format, ref pngMemory);
                Put(8 /* CF_DIB */, ref dibMemory);
                return true;
            }
            finally
            {
                if (opened) CloseClipboard();
                if (pngMemory != IntPtr.Zero) GlobalFree(pngMemory);
                if (dibMemory != IntPtr.Zero) GlobalFree(dibMemory);
            }
        }
        private static IntPtr Allocate(byte[] bytes)
        {
            IntPtr memory = GlobalAlloc(2 /* GMEM_MOVEABLE */, new UIntPtr((uint)bytes.Length));
            if (memory == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            IntPtr pointer = GlobalLock(memory);
            if (pointer == IntPtr.Zero) { GlobalFree(memory); throw new Win32Exception(Marshal.GetLastWin32Error()); }
            try { Marshal.Copy(bytes, 0, pointer, bytes.Length); }
            catch { GlobalUnlock(memory); GlobalFree(memory); throw; }
            GlobalUnlock(memory);
            return memory;
        }
        private static void Put(uint format, ref IntPtr memory)
        {
            if (SetClipboardData(format, memory) == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            memory = IntPtr.Zero; // Windows now owns the allocation.
        }

        private const string ObjC = "/usr/lib/libobjc.A.dylib";
        private const string AppKit = "/System/Library/Frameworks/AppKit.framework/AppKit";
        [DllImport(AppKit)] private static extern void NSApplicationLoad();
        [DllImport(ObjC)] private static extern IntPtr objc_getClass(string name);
        [DllImport(ObjC)] private static extern IntPtr sel_registerName(string name);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr receiver, IntPtr selector);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr String(IntPtr receiver, IntPtr selector, string value);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Data(IntPtr receiver, IntPtr selector, IntPtr bytes, UIntPtr count);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")]
        [return: MarshalAs(UnmanagedType.I1)] private static extern bool SetData(IntPtr receiver, IntPtr selector, IntPtr data, IntPtr type);
        private static void CopyMac(byte[] png)
        {
            NSApplicationLoad();
            IntPtr pool = Send(objc_getClass("NSAutoreleasePool"), sel_registerName("new"));
            GCHandle pin = GCHandle.Alloc(png, GCHandleType.Pinned);
            try
            {
                IntPtr pasteboard = Send(objc_getClass("NSPasteboard"), sel_registerName("generalPasteboard"));
                IntPtr type = String(objc_getClass("NSString"), sel_registerName("stringWithUTF8String:"), "public.png");
                IntPtr data = Data(objc_getClass("NSData"), sel_registerName("dataWithBytes:length:"), pin.AddrOfPinnedObject(), new UIntPtr((uint)png.Length));
                if (pasteboard == IntPtr.Zero || type == IntPtr.Zero || data == IntPtr.Zero)
                    throw new InvalidOperationException("The image pasteboard is unavailable.");
                Send(pasteboard, sel_registerName("clearContents"));
                if (!SetData(pasteboard, sel_registerName("setData:forType:"), data, type))
                    throw new InvalidOperationException("Could not copy the PNG to the pasteboard.");
            }
            finally { pin.Free(); Send(pool, sel_registerName("drain")); }
        }
        [DllImport("user32.dll", SetLastError = true)] private static extern bool OpenClipboard(IntPtr owner);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool EmptyClipboard();
        [DllImport("user32.dll")] private static extern bool CloseClipboard();
        [DllImport("user32.dll")] private static extern IntPtr GetActiveWindow();
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern uint RegisterClipboardFormat(string name);
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetClipboardData(uint format, IntPtr memory);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GlobalLock(IntPtr memory);
        [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr memory);
        [DllImport("kernel32.dll")] private static extern IntPtr GlobalFree(IntPtr memory);
    }
}
