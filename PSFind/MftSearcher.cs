﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Windows.Wdk.Storage.FileSystem;
using Windows.Win32;
using Windows.Win32.Storage.FileSystem;
using Windows.Win32.System.Ioctl;
using Microsoft.Win32.SafeHandles;

namespace PSFind;

class MftSearcher : IDisposable
{
    /// <summary>
    /// Contains the number of USN records analyzed during the last call to the <see cref="Search"/> method.
    /// </summary>
    public uint SearchedRecords { get; private set; }

    const int USN_SIZE = 8;
    const long ROOT_FRN = 0x5000000000005L;
    readonly char _volumeLetter;
    readonly SafeFileHandle _volumeHandle;


    /// <summary>
    /// Creates a new instance of <see cref="MftSearcher"/> for the given NTFS volume, allowing to instantly search files and folders in it
    /// </summary>
    /// <param name="volumeLetter">Letter corresponding to an NTFS volume</param>
    /// <exception cref="MftSearcherException"/>
    public MftSearcher(char volumeLetter)
    {
        _volumeLetter = volumeLetter;
        _volumeHandle = PInvoke.CreateFile($@"\\.\{_volumeLetter}:",
                                         (uint)FileAccess.Read,
                                         FILE_SHARE_MODE.FILE_SHARE_READ | FILE_SHARE_MODE.FILE_SHARE_WRITE,
                                         null,
                                         FILE_CREATION_DISPOSITION.OPEN_EXISTING,
                                         FILE_FLAGS_AND_ATTRIBUTES.SECURITY_ANONYMOUS,
                                         new SafeFileHandle());

        if (_volumeHandle.IsInvalid)
        {
            throw new MftSearcherException($"Couldn't get a valid handle to the '{volumeLetter}' drive", new Win32Exception(Marshal.GetLastPInvokeError()));
        }
    }

    public void Dispose()
    {
        _volumeHandle.Dispose();
        GC.SuppressFinalize(this);
    }

    public IEnumerable<string> Search(Predicate<string> predicate, bool folders)
    {
        SearchedRecords = 0;
        const int BUFFER_SIZE = 1024 * 1024; // 1MB - arbitrary value obtained from tests
        IntPtr pBuffer = Marshal.AllocHGlobal(BUFFER_SIZE);

        // Specify parameters for reading the MFT
        var mftEnumData = new MFT_ENUM_DATA_V0
        {
            StartFileReferenceNumber = 0,
            LowUsn = 0,
            HighUsn = long.MaxValue
        };

        try
        {
            uint bytesReturned;

            // Returned data won't fit inside the initial buffer, so it's necessary to make many calls to get all data
            // The do-while loop goes on until no more data is returned
            do
            {
                bytesReturned = ReadUsnDataIntoBuffer(pBuffer, BUFFER_SIZE, mftEnumData);

                if (bytesReturned == 0)
                {
                    break;
                }

                // Keep track of the position in the buffer
                uint bytesToRead = bytesReturned;
                // Get a pointer to the first record, which is right after the USN number
                IntPtr pUsnRecord = pBuffer + USN_SIZE;

                while (bytesToRead > USN_SIZE)
                {
                    var usnRecord = Marshal.PtrToStructure<USN_RECORD>(pUsnRecord);
                    bool isDirectory = (usnRecord.FileAttributes & (uint)FileAttributes.Directory) != 0;

                    // Filter out files or directories
                    if (isDirectory == folders)
                    {
                        // Get fileName of current record.
                        // The file name is not in a fixed position, so it's necessary to use the FileNameOffset and the buffer position to locate it
                        // FileNameLength is the length in bytes; since Unicode characters are 2 bytes each, it's necessary to divide by 2 to get the file name length
                        string name = Marshal.PtrToStringUni(pUsnRecord + usnRecord.FileNameOffset, usnRecord.FileNameLength / 2);

                        if (predicate(name))
                        {
                            yield return BuildPathFromMft(usnRecord.FileReferenceNumber);
                        }
                    }

                    // Set the usn record pointer to the next record in the buffer
                    pUsnRecord += (int)usnRecord.RecordLength;
                    // Update position in the buffer and searched records
                    bytesToRead -= usnRecord.RecordLength;
                    SearchedRecords++;
                }

                // The first 8 bytes are always the next USN.
                mftEnumData.StartFileReferenceNumber = (ulong)Marshal.ReadInt64(pBuffer);
            } while (bytesReturned > USN_SIZE);
        }
        finally
        {
            Marshal.FreeHGlobal(pBuffer);
        }
    }

    unsafe uint ReadUsnDataIntoBuffer(IntPtr pBuffer, int bufferSize, MFT_ENUM_DATA_V0 mftEnumData)
    {
        uint bytesReturned;
        PInvoke.DeviceIoControl(_volumeHandle,
                                PInvoke.FSCTL_ENUM_USN_DATA,
                                &mftEnumData,
                                (uint)Marshal.SizeOf(mftEnumData),
                                pBuffer.ToPointer(),
                                (uint)bufferSize,
                                &bytesReturned,
                                null);
        return bytesReturned;
    }

    // Recursively builds the full path for the file or directory associated with a given reference number, using the Master File Table
    unsafe string BuildPathFromMft(ulong referenceNumber)
    {
        // Check if the current fileID corresponds to the root directory, and in that case return the drive letter
        // This is also the stop condition for the recursion
        if (referenceNumber == ROOT_FRN)
        {
            return $"{_volumeLetter}:";
        }

        // Specify parameters for reading the MFT
        var mftEnumData = new MFT_ENUM_DATA_V0
        {
            StartFileReferenceNumber = referenceNumber,
            LowUsn = 0,
            HighUsn = long.MaxValue
        };
        uint mftEnumDataSize = (uint)Marshal.SizeOf(mftEnumData);

        // Buffer exact size can't be computed since the USN_RECORD size is variable (it depends on how long the file name is), but 512 bytes should be more than enough
        const int BUFFER_SIZE = 512;
        byte* buffer = stackalloc byte[BUFFER_SIZE];
        uint bytesReturned;

        PInvoke.DeviceIoControl(_volumeHandle,
                                PInvoke.FSCTL_ENUM_USN_DATA,
                                &mftEnumData,
                                mftEnumDataSize,
                                buffer,
                                BUFFER_SIZE,
                                &bytesReturned,
                                null);

        // The first 8 bytes in the buffer are the next USN number. The first returned USN_RECORD is next
        IntPtr pUsnRecord = new(buffer + USN_SIZE);
        var usnRecord = Marshal.PtrToStructure<USN_RECORD>(pUsnRecord);
        // Decode file name bytes as a Unicode string
        string name = Marshal.PtrToStringUni(pUsnRecord + usnRecord.FileNameOffset, usnRecord.FileNameLength / 2);

        if (usnRecord.FileReferenceNumber == referenceNumber)
        {
            // Recursive call to find the name of this file/folder parent
            return $"{BuildPathFromMft(usnRecord.ParentFileReferenceNumber)}\\{name}";
        }

        return name;
    }

    ~MftSearcher()
    {
        Dispose();
    }
}


class MftSearcherException(string message, Exception inner) : Exception(message, inner);
