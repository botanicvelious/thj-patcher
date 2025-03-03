using System;
using System.IO;
using System.Runtime.InteropServices;

namespace THJPatcher.Utilities
{
    public class PEModifier
    {
        private const ushort IMAGE_DOS_SIGNATURE = 0x5A4D;        // MZ
        private const uint IMAGE_NT_SIGNATURE = 0x00004550;       // PE00
        private const ushort LARGE_ADDRESS_AWARE = 0x0020;

        [StructLayout(LayoutKind.Sequential)]
        private struct IMAGE_DOS_HEADER
        {
            public ushort e_magic;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 58)]
            public byte[] e_fields;
            public int e_lfanew;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IMAGE_FILE_HEADER
        {
            public ushort Machine;
            public ushort NumberOfSections;
            public uint TimeDateStamp;/q
            public uint PointerToSymbolTable;
            public uint NumberOfSymbols;
            public ushort SizeOfOptionalHeader;
            public ushort Characteristics;
        }

        public static bool Apply4GBPatch(string exePath)
        {
            try
            {
                // Create backup first
                string backupPath = exePath + ".bak";
                if (!File.Exists(backupPath))
                {
                    File.Copy(exePath, backupPath, false);
                }

                byte[] fileBytes = File.ReadAllBytes(exePath);

                var dosHeader = ByteArrayToStructure<IMAGE_DOS_HEADER>(fileBytes);
                if (dosHeader.e_magic != IMAGE_DOS_SIGNATURE)
                    return false;

                int peOffset = dosHeader.e_lfanew;
                if (peOffset > fileBytes.Length - 4)
                    return false;

                uint peSignature = BitConverter.ToUInt32(fileBytes, peOffset);
                if (peSignature != IMAGE_NT_SIGNATURE)
                    return false;

                int fileHeaderOffset = peOffset + 4;
                var fileHeader = ByteArrayToStructure<IMAGE_FILE_HEADER>(fileBytes, fileHeaderOffset);

                fileHeader.Characteristics |= LARGE_ADDRESS_AWARE;

                byte[] modifiedHeader = StructureToByteArray(fileHeader);
                Buffer.BlockCopy(modifiedHeader, 0, fileBytes, fileHeaderOffset, modifiedHeader.Length);

                File.WriteAllBytes(exePath, fileBytes);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static T ByteArrayToStructure<T>(byte[] bytes, int offset = 0) where T : struct
        {
            int size = Marshal.SizeOf<T>();
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.Copy(bytes, offset, ptr, size);
                return Marshal.PtrToStructure<T>(ptr);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }

        private static byte[] StructureToByteArray<T>(T structure) where T : struct
        {
            int size = Marshal.SizeOf<T>();
            byte[] arr = new byte[size];
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(structure, ptr, true);
                Marshal.Copy(ptr, arr, 0, size);
                return arr;
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
    }
} 