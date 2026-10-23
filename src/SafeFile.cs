// Writes into user-writable folders (%APPDATA%\HPControlPanel, the per-user Start menu) from this elevated app.
// A program running as the same user could turn such a folder or file into a junction, symbolic link or hard link,
// so that an administrator write lands somewhere else (for example in System32). Every write therefore checks the
// folder, opens the file, and verifies by handle that it is the expected single-link file before changing it;
// anything else is refused and the write is skipped. Reads from such folders get the same check (ReadAllBytes), so
// a link can't make the elevated app copy out a file the user couldn't read themselves.
using System;
using System.IO;
using System.Text;
using System.Security.AccessControl;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace HPControlPanel
{
    public static class SafeFile
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr sa, uint disposition, uint flags, IntPtr template);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern uint GetFinalPathNameByHandle(SafeFileHandle h, StringBuilder path, uint size, uint flags);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetFileInformationByHandle(SafeFileHandle h, out BY_HANDLE_FILE_INFORMATION info);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool SetFileInformationByHandle(SafeFileHandle h, int infoClass, ref byte deleteFile, uint size);

        [StructLayout(LayoutKind.Sequential, Pack = 4)]   // FILETIMEs are two DWORDs: no 8-byte alignment
        struct BY_HANDLE_FILE_INFORMATION
        {
            public uint Attributes; public long Created, Accessed, Written;
            public uint VolumeSerial, SizeHigh, SizeLow, NumberOfLinks, IndexHigh, IndexLow;
        }

        static string FinalPath(SafeFileHandle h)
        {
            var sb = new StringBuilder(1024);
            uint n = GetFinalPathNameByHandle(h, sb, (uint)sb.Capacity, 0);
            if (n == 0 || n >= sb.Capacity) return null;
            string p = sb.ToString();
            return p.StartsWith(@"\\?\") && !p.StartsWith(@"\\?\UNC\") ? p.Substring(4) : p;
        }

        static bool Same(string a, string b)
        {
            return a != null && string.Equals(a.TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }

        // The folder resolves to itself: no junction or symbolic link on it or on any folder above it.
        public static bool DirOk(string dir)
        {
            try
            {
                using (var h = CreateFile(dir, 0x80 /*FILE_READ_ATTRIBUTES*/, 7, IntPtr.Zero, 3 /*OPEN_EXISTING*/, 0x02000000 /*FILE_FLAG_BACKUP_SEMANTICS*/, IntPtr.Zero))
                    return !h.IsInvalid && Same(FinalPath(h), dir);
            }
            catch { return false; }
        }

        // The handle is the expected file: same final path, and a single link (not a hard link to some other file).
        static bool HandleOk(FileStream fs, string full)
        {
            BY_HANDLE_FILE_INFORMATION info;
            return Same(FinalPath(fs.SafeFileHandle), full) && GetFileInformationByHandle(fs.SafeFileHandle, out info) && info.NumberOfLinks == 1;
        }

        static FileStream Open(string path, bool append)
        {
            string full = Path.GetFullPath(path);
            if (!DirOk(Path.GetDirectoryName(full))) return null;
            if (File.Exists(full) && (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0) return null;
            // Open an existing file without truncating it; create only a file that doesn't exist yet. If the folder was
            // swapped for a link after DirOk, the check below fails and a file this call created is deleted again,
            // so a refused write leaves nothing behind (not even an empty file) in the place the link pointed to.
            const FileSystemRights rights = FileSystemRights.Write | FileSystemRights.Delete;
            FileStream fs;
            bool created = false;
            try { fs = new FileStream(full, FileMode.Open, rights, FileShare.Read, 4096, FileOptions.None); }
            catch (FileNotFoundException)
            {
                fs = new FileStream(full, FileMode.CreateNew, rights, FileShare.Read, 4096, FileOptions.None);
                created = true;
            }
            if (!HandleOk(fs, full))
            {
                if (created) { byte del = 1; SetFileInformationByHandle(fs.SafeFileHandle, 4 /*FileDispositionInfo*/, ref del, 1); }
                fs.Dispose();
                return null;
            }
            if (append) fs.Seek(0, SeekOrigin.End);
            else fs.SetLength(0);   // truncate only after the check
            return fs;
        }

        // Reads a file from a user-writable folder with the same folder / final-path / link checks as a write, from the
        // checked handle itself, and refuses anything larger than maxBytes. Returns null when refused or unreadable.
        public static byte[] ReadAllBytes(string path, long maxBytes)
        {
            try
            {
                string full = Path.GetFullPath(path);
                if (!DirOk(Path.GetDirectoryName(full))) return null;
                if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0) return null;
                using (var fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (!HandleOk(fs, full) || fs.Length > maxBytes) return null;
                    var data = new byte[fs.Length];
                    int n = 0, r;
                    while (n < data.Length && (r = fs.Read(data, n, data.Length - n)) > 0) n += r;
                    return n == data.Length ? data : null;
                }
            }
            catch { return null; }
        }

        public static bool WriteAllBytes(string path, byte[] data)
        {
            try
            {
                using (var fs = Open(path, false))
                {
                    if (fs == null) return false;
                    fs.Write(data, 0, data.Length);
                    return true;
                }
            }
            catch { return false; }
        }

        public static bool WriteAllText(string path, string text)
        {
            return WriteAllBytes(path, new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(text)));
        }

        public static bool AppendAllText(string path, string text)
        {
            try
            {
                using (var fs = Open(path, true))
                {
                    if (fs == null) return false;
                    var b = Encoding.UTF8.GetBytes(text);
                    fs.Write(b, 0, b.Length);
                    return true;
                }
            }
            catch { return false; }
        }

        static byte[] Concat(this byte[] a, byte[] b)
        {
            var r = new byte[a.Length + b.Length];
            Buffer.BlockCopy(a, 0, r, 0, a.Length);
            Buffer.BlockCopy(b, 0, r, a.Length, b.Length);
            return r;
        }
    }
}
