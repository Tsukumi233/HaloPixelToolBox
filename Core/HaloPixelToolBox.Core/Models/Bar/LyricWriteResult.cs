namespace HaloPixelToolBox.Core.Models.Bar;

public enum LyricWriteStatus
{
    Success,
    NoChange,
    TransportAcceptedUnconfirmed,
    DeviceUnavailable,
    UnsupportedDevice,
    IoError
}

public readonly record struct LyricWriteResult(
    LyricWriteStatus Status,
    bool TransitionWritten = false,
    bool TransitionConfirmed = false,
    bool TextWritten = false,
    int Attempts = 0)
{
    public bool Succeeded => Status is
        LyricWriteStatus.Success or
        LyricWriteStatus.NoChange or
        LyricWriteStatus.TransportAcceptedUnconfirmed;
}
