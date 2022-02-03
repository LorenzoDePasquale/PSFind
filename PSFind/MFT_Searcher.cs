using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace PSFind;

public class MFT_Searcher : IDisposable
{
    /// <summary>
    /// Contains the amount of USN records analyzed during the last call to the <see cref="Search"/> method.
    /// </summary>
    public uint SearchedRecords { get; private set; }

    const int USN_SIZE = 8;
    const long ROOT_FRN = 0x5000000000005L;
    readonly char _volumeLetter;
    readonly IntPtr _volumeHandle;


    /// <summary>
    /// Creates a new instance of <see cref="MFT_Searcher"/> for the given NTFS volume, allowing to instantly search file and folders in it
    /// </summary>
    /// <param name="volumeLetter">Letter corresponding to a NTFS volume</param>
    /// <exception cref="MFT_SearcherException"/>
    public MFT_Searcher(char volumeLetter)
    {
        _volumeLetter = volumeLetter;
        _volumeHandle = Win32.CreateFile($@"\\.\{_volumeLetter}:", FileAccess.Read, FileShare.ReadWrite, IntPtr.Zero, FileMode.Open, 0, IntPtr.Zero);

        if (_volumeHandle == Win32.INVALID_HANDLE_VALUE)
        {
            throw new MFT_SearcherException($"Couldn't get a valid handle to the '{volumeLetter}' drive", new Win32Exception(Marshal.GetLastWin32Error()));
        }
    }

    public IEnumerable<string> Search(string fileName, bool folders = false)
    {
        string pattern = $"^{Regex.Escape(fileName).Replace(@"\*", ".*").Replace(@"\?", ".")}$";

        return Search_Internal(pattern, folders);
    }

    public IEnumerable<string> SearchPattern(string pattern, bool folders = false) => Search_Internal(pattern, folders);

    public void Dispose()
    {
        if (_volumeHandle != IntPtr.Zero && _volumeHandle != Win32.INVALID_HANDLE_VALUE)
        {
            Win32.CloseHandle(_volumeHandle);
        }

        // If dispose has already been called, there's no need to call the finalizer
        GC.SuppressFinalize(this);
    }


    private IEnumerable<string> Search_Internal(string pattern, bool folders)
    {
        SearchedRecords = 0;
        FileAttributes searchAttribute = folders ? FileAttributes.Directory : 0;

        // Regex is compiled since it will be reused many times
        var regex = new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

        int bufferSize = 1024 * 1024; // 1MB - arbitrary value obtained from tests

        // Allocate a buffer to store MFT records
        IntPtr pBuffer = Marshal.AllocHGlobal(bufferSize);

        // Specify parameters for reading the MFT
        var mftEnumData = new Win32.MFT_ENUM_DATA_V0
        {
            StartFileReferenceNumber = 0,
            LowUsn = 0,
            HighUsn = long.MaxValue
        };

        uint mftEnumDataSize = (uint)Marshal.SizeOf(mftEnumData);
        int bytesReturned;

        // Returned data won't fit inside the inital buffer, so it's necessary to make many subsequent calls to get all data
        // The do-while loop goes on until no more data is returned
        do
        {
            if (Win32.DeviceIoControl(_volumeHandle, Win32.FSCTL_ENUM_USN_DATA, ref mftEnumData, mftEnumDataSize, pBuffer, (uint)bufferSize, out bytesReturned, IntPtr.Zero))
            {
                // Keep track of the position in the buffer
                int bytesToRead = bytesReturned;

                // Get a pointer to the first record, which is right after the USN number
                IntPtr pUsnRecord = new(pBuffer.ToInt64() + USN_SIZE);

                while (bytesToRead > USN_SIZE)
                {
                    // Copy pointer to USN_RECORD structure
                    var usnRecord = PtrToStructure<Win32.USN_RECORD>(pUsnRecord);

                    // Filter out files or directories
                    if ((usnRecord.FileAttributes & FileAttributes.Directory) == searchAttribute)
                    {
                        // Get fileName of current record.
                        // The file name is not in a fixed position, so it's necessary to use the FileNameOffset and the buffer position to locate it
                        // FileNameLength is the length in bytes; since Unicode characters are 2 bytes each, it's necessary to divide by 2 to get the file name length
                        string name = Marshal.PtrToStringUni(new IntPtr(pUsnRecord.ToInt64() + usnRecord.FileNameOffset), usnRecord.FileNameLength / 2);

                        if (regex.IsMatch(name))
                        {
                            yield return BuildPathFromMFT(usnRecord.FileReferenceNumber);
                        }
                    }

                    // Set the usn record pointer to the next record in the buffer
                    pUsnRecord = new IntPtr(pUsnRecord.ToInt64() + usnRecord.RecordLength);

                    // Update position in the buffer and searched records
                    bytesToRead -= (int)usnRecord.RecordLength;
                    SearchedRecords++;
                }

                // The first 8 bytes are always the next USN.
                mftEnumData.StartFileReferenceNumber = (ulong)Marshal.ReadInt64(pBuffer);
            }
            else
            {
                break;
            }
        }
        while (bytesReturned > USN_SIZE);

        // Free unmanaged memory
        Marshal.FreeHGlobal(pBuffer);
    }

    // Recursively builds the full path for the file or directory associated with a given reference number, using the Master File Table
    private string BuildPathFromMFT(ulong referenceNumber)
    {
        // Check if current fileID corresponds to the root directory, and in that case return the drive letter
        // This is also the stop condition for the recursion
        if (referenceNumber == ROOT_FRN)
        {
            return $"{_volumeLetter}:";
        }

        // Specify parameters for reading the MFT
        var mftEnumData = new Win32.MFT_ENUM_DATA_V0
        {
            StartFileReferenceNumber = referenceNumber,
            LowUsn = 0,
            HighUsn = long.MaxValue
        };
        uint mftEnumDataSize = (uint)Marshal.SizeOf(mftEnumData);

        // Buffer exact size can't be computed since the USN_RECORD size is variable (it depends on how long the file name is), but 512 bytes should be more than enough
        Span<byte> buffer = stackalloc byte[512];

        Win32.DeviceIoControl(_volumeHandle,
                              Win32.FSCTL_ENUM_USN_DATA,
                              ref mftEnumData,
                              mftEnumDataSize,
                              ref MemoryMarshal.GetReference(buffer),
                              (uint)buffer.Length,
                              out _,
                              IntPtr.Zero);

        // Get a USN record structure from the span; first 8 bytes are the next USN number
        var usnRecord = MemoryMarshal.Read<Win32.USN_RECORD>(buffer[USN_SIZE..]);

        // The file name is not in a fixed position, so it's necessary to use the FileNameOffset and FileNameLength to locate it
        var nameSpan = buffer.Slice(USN_SIZE + usnRecord.FileNameOffset, usnRecord.FileNameLength);

        // Decode file name bytes as an Unicode string
        string name = System.Text.Encoding.Unicode.GetString(nameSpan);

        if (usnRecord.FileReferenceNumber == referenceNumber)
        {
            // Recursive call to find the name of this file/folder parent
            return $"{BuildPathFromMFT(usnRecord.ParentFileReferenceNumber)}\\{name}";
        }
        else
        {
            return name;
        }
    }

    private static T PtrToStructure<T>(IntPtr structPtr) where T : struct
    {
        return (T)Marshal.PtrToStructure(structPtr, typeof(T));
    }

    ~MFT_Searcher()
    {
        // Ensure that Dispose is called
        Dispose();
    }
}
