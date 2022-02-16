using System;
using System.IO;
using System.Runtime.InteropServices;

namespace PSFind
{
    internal static class Win32
    {
        internal static IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        /// <summary>
        /// Enumerates the update sequence number (USN) data between two specified boundaries to obtain master file table (MFT) records.
        /// </summary>
        internal const int FSCTL_ENUM_USN_DATA = 0x000900b3;

        [Flags]
        internal enum USN_REASON : uint
        {
            DATA_OVERWRITE = 0x00000001,
            DATA_EXTEND = 0x00000002,
            DATA_TRUNCATION = 0x00000004,
            NAMED_DATA_OVERWRITE = 0x00000010,
            NAMED_DATA_EXTEND = 0x00000020,
            NAMED_DATA_TRUNCATION = 0x00000040,
            FILE_CREATE = 0x00000100,
            FILE_DELETE = 0x00000200,
            EA_CHANGE = 0x00000400,
            SECURITY_CHANGE = 0x00000800,
            RENAME_OLD_NAME = 0x00001000,
            RENAME_NEW_NAME = 0x00002000,
            INDEXABLE_CHANGE = 0x00004000,
            BASIC_INFO_CHANGE = 0x00008000,
            HARD_LINK_CHANGE = 0x00010000,
            COMPRESSION_CHANGE = 0x00020000,
            ENCRYPTION_CHANGE = 0x00040000,
            OBJECT_ID_CHANGE = 0x00080000,
            REPARSE_POINT_CHANGE = 0x00100000,
            STREAM_CHANGE = 0x00200000,
            CLOSE = 0x80000000
        }

        [Flags]
        internal enum USNJournalReason : uint
        {
            DataOverwrite = 0x00000001,
            DataExtend = 0x00000002,
            DataTruncation = 0x00000004,
            NamedDataOverwrite = 0x00000010,
            NamedDataExtend = 0x00000020,
            NamedDataTruncation = 0x00000040,
            FileCreate = 0x00000100,
            FileDelete = 0x00000200,
            EAChange = 0x00000400,
            SecurityChange = 0x00000800,
            RenameOldName = 0x00001000,
            RenameNewName = 0x00002000,
            IndexableChange = 0x00004000,
            BasicInfoChange = 0x00008000,
            HardLinkChange = 0x00010000,
            CompressionChange = 0x00020000,
            EncryptionChange = 0x00040000,
            ObjectIDChange = 0x00080000,
            ReparsePointChange = 0x00100000,
            StreamChange = 0x00200000,
            Close = 0x80000000
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct USN_RECORD
        {
            internal uint RecordLength;
            internal ushort MajorVersion;
            internal ushort MinorVersion;
            internal ulong FileReferenceNumber;
            internal ulong ParentFileReferenceNumber;
            internal long Usn;
            internal long TimeStamp;  // This is a LARGE_INTEGER in C, converted as long for simplicity
            internal USNJournalReason Reason;
            internal uint SourceInfo;
            internal uint SecurityId;
            internal FileAttributes FileAttributes;
            internal ushort FileNameLength;
            internal ushort FileNameOffset;

            // DO NOT ASSUME THE FILENAME COMES NEXT, use the FileNameOffset field!
            // The FileNameOffset is relative to the beginning of the structure
            // Use the RecordLength to find the beginning of the next record, which
            // is also relative to the beginning of the structure
            // Note that the FileNameLength length is in bytes, not in (wide) characters
        }

        /// <summary>
        /// Contains information defining the boundaries and starting place of an enumeration of update sequence number (USN) change journal records. 
        /// It is used as the input buffer for the FSCTL_ENUM_USN_DATA control code. 
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct MFT_ENUM_DATA_V0
        {
            internal ulong StartFileReferenceNumber;
            internal long LowUsn;
            internal long HighUsn;
        }


        /// <summary>
        /// Creates or opens a file or I/O device.
        /// </summary>
        /// <param name="filename">The name of the file or device to be created or opened.</param>
        /// <param name="access">The requested access to the file or device.</param>
        /// <param name="share">The requested sharing mode of the file or device. If this parameter is zero and CreateFile succeeds, the file or device cannot be shared and cannot be opened again until the handle to the file or device is closed.</param>
        /// <param name="securityAttributes">A pointer to a SECURITY_ATTRIBUTES structure that contains two separate but related data members: an optional security descriptor, and a Boolean value that determines whether the returned handle can be inherited by child processes. This parameter can be NULL.</param>
        /// <param name="creationDisposition">An action to take on a file or device that exists or does not exist.</param>
        /// <param name="flagsAndAttributes">The file or device attributes and flags.</param>
        /// <param name="templateFile">A valid handle to a template file with the GENERIC_READ access right. The template file supplies file attributes and extended attributes for the file that is being created. This parameter can be NULL.</param>
        /// <returns>If the function succeeds, the return value is an open handle to the specified file, device, or named pipe. If the function fails, the return value is INVALID_HANDLE_VALUE.</returns>
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr CreateFile(string filename, FileAccess access, FileShare share, IntPtr securityAttributes, FileMode creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

        /// <summary>
        /// Sends a control code directly to a specified device driver, causing the corresponding device to perform the corresponding operation.
        /// </summary>
        /// <param name="hDevice">A handle to the device on which the operation is to be performed. The device is typically a volume, directory, file, or stream. To retrieve a device handle, use the CreateFile function.</param>
        /// <param name="dwIoControlCode">The control code for the operation. This value identifies the specific operation to be performed and the type of device on which to perform it.</param>
        /// <param name="lpInBuffer">A pointer to the input buffer that contains the data required to perform the operation. The format of this data depends on the value of the dwIoControlCode parameter.</param>
        /// <param name="nInBufferSize">The size of the input buffer, in bytes.</param>
        /// <param name="lpOutBuffer">A pointer to the output buffer that is to receive the data returned by the operation. The format of this data depends on the value of the dwIoControlCode parameter.</param>
        /// <param name="nOutBufferSize">The size of the output buffer, in bytes.</param>
        /// <param name="lpBytesReturned">A variable that receives the size of the data stored in the output buffer, in bytes.</param>
        /// <param name="lpOverlapped">A pointer to an OVERLAPPED structure.</param>
        [DllImport("Kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern bool DeviceIoControl(IntPtr hDevice, int dwIoControlCode, ref MFT_ENUM_DATA_V0 lpInBuffer, uint nInBufferSize, IntPtr lpOutBuffer, uint nOutBufferSize, out int lpBytesReturned, IntPtr lpOverlapped);

        /// <summary>
        /// Sends a control code directly to a specified device driver, causing the corresponding device to perform the corresponding operation.
        /// </summary>
        /// <param name="hDevice">A handle to the device on which the operation is to be performed. The device is typically a volume, directory, file, or stream. To retrieve a device handle, use the CreateFile function.</param>
        /// <param name="dwIoControlCode">The control code for the operation. This value identifies the specific operation to be performed and the type of device on which to perform it.</param>
        /// <param name="lpInBuffer">A pointer to the input buffer that contains the data required to perform the operation. The format of this data depends on the value of the dwIoControlCode parameter.</param>
        /// <param name="nInBufferSize">The size of the input buffer, in bytes.</param>
        /// <param name="lpOutBuffer">A pointer to the output buffer that is to receive the data returned by the operation. The format of this data depends on the value of the dwIoControlCode parameter.</param>
        /// <param name="nOutBufferSize">The size of the output buffer, in bytes.</param>
        /// <param name="lpBytesReturned">A variable that receives the size of the data stored in the output buffer, in bytes.</param>
        /// <param name="lpOverlapped">A pointer to an OVERLAPPED structure.</param>
        [DllImport("Kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern bool DeviceIoControl(IntPtr hDevice, int dwIoControlCode, ref MFT_ENUM_DATA_V0 lpInBuffer, uint nInBufferSize, ref byte lpOutBuffer, uint nOutBufferSize, out int lpBytesReturned, IntPtr lpOverlapped);

        [DllImport("Kernel32.dll", SetLastError = true)]
        internal static extern bool CloseHandle(IntPtr handle);
    }
}
