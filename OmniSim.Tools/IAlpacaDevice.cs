namespace GS.Simulator
{
    public interface IAlpacaDevice
    {
        string DeviceName { get; }
        int DeviceNumber { get; }
        string UniqueID { get; }
    }
}