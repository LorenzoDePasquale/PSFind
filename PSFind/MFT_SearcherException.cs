using System;

namespace PSFind;

public class MFT_SearcherException : Exception
{
    public MFT_SearcherException() { }
    public MFT_SearcherException(string message) : base(message) { }
    public MFT_SearcherException(string message, Exception inner) : base(message, inner) { }
}
